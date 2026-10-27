using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nut.Results;
using Shuffull.Api.Configuration;
using Shuffull.Core.Alerts;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence;
using Shuffull.Core.Services.Alerts;

namespace Shuffull.Api.Services.Alerts;

/// <summary>
/// Every few minutes: run the probes, run the checks, and email what started, got worse, is due a reminder or has
/// cleared (Shuffull#75). The funnel runs the same monitor over its own DB (YoutubeFunnel#31).
///
/// <para>The <see cref="AlertState"/> row is updated only after its email went out, so a failed send is simply
/// retried next run, and a restart never re-sends what was already sent. A check that can't run leaves its open
/// alerts alone and records a <c>monitor:</c> health signal, which alerts if it keeps failing.</para>
///
/// <para>The mailer is resolved per run from the run's scope, so the Resend client behind it comes fresh from
/// IHttpClientFactory each time instead of one client being pinned for the life of the process.</para>
/// </summary>
public sealed class AlertMonitor(
    IServiceScopeFactory scopeFactory,
    IHealthSignals signals,
    AlertsConfiguration config,
    TimeProvider timeProvider,
    ILogger<AlertMonitor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(config.CanSendEmail
            ? "Alert monitor started; emailing {To} every {Interval} when something needs attention."
            : "Alert monitor started without email (no Alerts:ResendApiKey or Alerts:To); alerts go to this log. Every {Interval}.",
            config.To, config.Interval);

        try
        {
            await Task.Delay(config.StartupDelay, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (Exception e) when (e is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(e, "The alert monitor run failed; trying again next interval.");
                }

                await Task.Delay(config.Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        using var scope = scopeFactory.CreateScope();
        var services = scope.ServiceProvider;

        foreach (var probe in services.GetServices<IAlertProbe>())
        {
            try
            {
                await probe.RunAsync(now, cancellationToken);
            }
            catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(e, "The alert probe {Probe} threw.", probe.GetType().Name);
            }
        }

        var db = services.GetRequiredService<ShuffullContext>();
        var mailer = services.GetRequiredService<IAlertMailer>();
        var states = await db.AlertStates.ToDictionaryAsync(s => s.Key, cancellationToken);

        foreach (var check in services.GetServices<IAlertCheck>())
        {
            var name = check.GetType().Name;
            Result<IReadOnlyList<AlertFinding>> result;
            try
            {
                result = await check.EvaluateAsync(now, cancellationToken);
            }
            catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                result = Result.Error<IReadOnlyList<AlertFinding>>(e);
            }

            if (result.IsError)
            {
                logger.LogWarning("The alert check {Check} could not run: {Error}", name, result.GetError().Message);
                await signals.FailureAsync($"monitor:{check.KeyPrefix}", $"{name}: {result.GetError().Message}", cancellationToken);
                continue;
            }

            await signals.SuccessAsync($"monitor:{check.KeyPrefix}", cancellationToken);
            await ApplyAsync(db, mailer, states, check, result.Get(), now, cancellationToken);
        }
    }

    private async Task ApplyAsync(ShuffullContext db, IAlertMailer mailer, Dictionary<string, AlertState> states, IAlertCheck check,
        IReadOnlyList<AlertFinding> findings, DateTime now, CancellationToken cancellationToken)
    {
        var prefix = check.KeyPrefix + ":";
        var byKey = new Dictionary<string, AlertFinding>();
        foreach (var finding in findings)
        {
            if (!finding.Key.StartsWith(prefix, StringComparison.Ordinal) || finding.Key.Length > AlertState.MaxKeyLength)
            {
                logger.LogError("Alert check {Check} reported key {Key}, outside its prefix {Prefix} or too long; ignored.",
                    check.GetType().Name, finding.Key, prefix);
                continue;
            }

            // Two findings for one key: keep the worse.
            if (!byKey.TryGetValue(finding.Key, out var existing) || finding.Level > existing.Level)
            {
                byKey[finding.Key] = finding;
            }
        }

        var keys = states.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).Union(byKey.Keys).ToList();
        foreach (var key in keys)
        {
            states.TryGetValue(key, out var state);
            byKey.TryGetValue(key, out var finding);
            var decision = AlertEvaluator.Decide(state, finding, now, config.ResolveAfter);
            if (decision == AlertDecisionKind.None)
            {
                continue;
            }

            if (state == null)
            {
                state = AlertState.Create(key);
                db.AlertStates.Add(state);
                states[key] = state;
            }

            switch (decision)
            {
                case AlertDecisionKind.Fire or AlertDecisionKind.Escalate or AlertDecisionKind.Remind when finding != null:
                    var subject = decision == AlertDecisionKind.Remind ? $"Still happening: {finding.Subject}" : finding.Subject;
                    var firstSeen = state.FiringSinceUtc ?? now;
                    var reminders = finding.RemindEvery is { } every
                        ? $", a reminder every {Describe(every)} while it lasts,"
                        : string.Empty;
                    var body = $"{finding.Body}\n\n---\nFirst seen: {firstSeen:yyyy-MM-dd HH:mm} UTC\nAlert: {key}\n" +
                               $"You get one email when this starts, one if it gets worse{reminders} and one when it clears.";
                    if (await SendAsync(mailer, subject, body, cancellationToken))
                    {
                        state.Notified(finding.Level, finding.Subject, now);
                    }
                    break;
                case AlertDecisionKind.Update when finding != null:
                    state.StillFiring(finding.Level, finding.Subject);
                    break;
                case AlertDecisionKind.StartClearing:
                    state.StartedClearing(now);
                    break;
                case AlertDecisionKind.Resolve:
                    var since = state.FiringSinceUtc is { } f ? $" It started {f:yyyy-MM-dd HH:mm} UTC." : string.Empty;
                    if (await SendAsync(mailer, $"Resolved: {state.Subject}",
                            $"The check no longer reports this.{since} Usually that means it was fixed; a failing health " +
                            $"signal that nothing has recorded for a week also stops alerting.\n\n---\nAlert: {key}",
                            cancellationToken))
                    {
                        state.Resolved(now);
                    }
                    break;
            }

            // Saved per key, so a later failure in this run can't undo the record of an email already sent.
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static string Describe(TimeSpan span)
    {
        if (span.TotalDays >= 1)
        {
            return span.TotalDays == 1 ? "day" : $"{span.TotalDays:0} days";
        }

        return $"{span.TotalHours:0} hours";
    }

    private async Task<bool> SendAsync(IAlertMailer mailer, string subject, string body, CancellationToken cancellationToken)
    {
        var prefixed = string.IsNullOrWhiteSpace(config.SubjectPrefix) ? subject : $"{config.SubjectPrefix} {subject}";
        var result = await mailer.SendAsync(prefixed, body, cancellationToken);
        return result.IsOk;
    }
}
