using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence;
using Shuffull.Core.Services.Alerts;

namespace Shuffull.Core.Alerts.Checks;

/// <summary>
/// Turns <see cref="HealthSignal"/> rows into alerts through <see cref="ShuffullSignalRules"/>. The first matching
/// rule wins; a signal no rule matches is recorded but never alerts.
///
/// <para>Silence is measured from the later of a signal's last record and <c>trackingSinceUtc</c>, the API's start.
/// While the API was down nothing could record, so a funnel poll's silence then says nothing about the funnel, and
/// the first run after an outage would otherwise email "the funnel stopped" for both polls. A rule for one exact
/// key that watches for silence also fires when its row doesn't exist yet: a funnel that never polls after a deploy
/// (a wrong ImportKey from day one) would otherwise never write a row to go stale.</para>
///
/// <para>A failing signal that nothing has recorded for <see cref="RetireAfter"/> stops alerting, so its alert
/// resolves. Keys can name something that goes away (a refused Keep's song once the user stops re-sending it), and
/// only a success on the same key would otherwise ever clear it. Stale rules are exempt: "stopped happening" is
/// exactly a signal nobody records, and must keep reminding.</para>
/// </summary>
public sealed class SignalAlertCheck(ShuffullContext db, IReadOnlyList<SignalRule> rules, DateTime trackingSinceUtc) : IAlertCheck
{
    public SignalAlertCheck(ShuffullContext db, DateTime trackingSinceUtc) : this(db, ShuffullSignalRules.All, trackingSinceUtc)
    {
    }

    public static readonly TimeSpan RetireAfter = TimeSpan.FromDays(7);

    public string KeyPrefix => "signal";

    public async Task<Result<IReadOnlyList<AlertFinding>>> EvaluateAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var signalRows = await db.HealthSignals.AsNoTracking().ToListAsync(cancellationToken);
        var findings = new List<AlertFinding>();

        foreach (var signal in signalRows)
        {
            var rule = rules.FirstOrDefault(r => r.Matches(signal.Key));
            if (rule == null)
            {
                continue;
            }

            if (rule.IsStale(signal.LastSeenUtc, nowUtc, trackingSinceUtc))
            {
                findings.Add(Stale(rule, signal.Key, nowUtc,
                    $"Nothing has been recorded for {signal.Key} since {signal.LastSeenUtc:yyyy-MM-dd HH:mm} UTC " +
                    $"({Ago(nowUtc - signal.LastSeenUtc)})."));
            }
            else if (rule.FailingSubject is { } subjectOf && rule.IsFailing(signal, nowUtc)
                     && nowUtc - signal.LastSeenUtc < RetireAfter)
            {
                findings.Add(new AlertFinding($"signal:failing:{signal.Key}", rule.Level, subjectOf(signal),
                    $"{Describe(rule, signal, nowUtc)}\n\n{rule.Hint}", rule.RemindEvery));
            }
        }

        var recorded = signalRows.Select(s => s.Key).ToHashSet();
        foreach (var rule in rules.Where(r => r.ExpectsExactKey && !recorded.Contains(r.Key)))
        {
            if (rule.IsStale(trackingSinceUtc, nowUtc, trackingSinceUtc))
            {
                findings.Add(Stale(rule, rule.Key, nowUtc,
                    $"Nothing has been recorded for {rule.Key} since the API started at {trackingSinceUtc:yyyy-MM-dd HH:mm} UTC " +
                    $"({Ago(nowUtc - trackingSinceUtc)}), and never before that either."));
            }
        }

        return Result.Ok<IReadOnlyList<AlertFinding>>(findings);
    }

    private static AlertFinding Stale(SignalRule rule, string key, DateTime nowUtc, string what)
    {
        var subject = rule.StaleSubject is { } subjectOf ? subjectOf(key) : $"{key} stopped happening";
        return new AlertFinding($"signal:stale:{key}", rule.Level, subject, $"{what}\n\n{rule.Hint}", rule.RemindEvery);
    }

    private static string Describe(SignalRule rule, HealthSignal signal, DateTime nowUtc)
    {
        if (rule.RecentWithin != null)
        {
            // An event signal: nothing records success, so the counter is every occurrence since the first.
            return $"Latest at {signal.LastFailureUtc:yyyy-MM-dd HH:mm} UTC: {signal.LastError}\n" +
                   $"{signal.ConsecutiveFailures} time(s) in total since {signal.FailingSinceUtc:yyyy-MM-dd HH:mm} UTC.";
        }

        var lastSuccess = signal.LastSuccessUtc is { } ok
            ? $"{ok:yyyy-MM-dd HH:mm} UTC ({Ago(nowUtc - ok)})"
            : "never recorded";
        return $"{signal.ConsecutiveFailures} failure(s) in a row since {signal.FailingSinceUtc:yyyy-MM-dd HH:mm} UTC.\n" +
               $"Last success: {lastSuccess}.\nLast error: {signal.LastError}";
    }

    internal static string Ago(TimeSpan span)
    {
        if (span.TotalDays >= 2)
        {
            return $"{(int)span.TotalDays} days ago";
        }

        if (span.TotalHours >= 2)
        {
            return $"{(int)span.TotalHours} hours ago";
        }

        return $"{Math.Max(0, (int)span.TotalMinutes)} minutes ago";
    }
}
