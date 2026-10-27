using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence;

namespace Shuffull.Core.Services.Alerts.Checks;

/// <summary>
/// Turns <see cref="HealthSignal"/> rows into alerts through <see cref="ShuffullSignalRules"/>. The first matching
/// rule wins; a signal no rule matches is recorded but never alerts.
/// </summary>
public sealed class SignalAlertCheck(ShuffullContext db, IReadOnlyList<SignalRule> rules) : IAlertCheck
{
    public SignalAlertCheck(ShuffullContext db) : this(db, ShuffullSignalRules.All)
    {
    }

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

            if (rule.IsStale(signal, nowUtc))
            {
                var subject = rule.StaleSubject?.Invoke(signal) ?? $"{signal.Key} stopped happening";
                findings.Add(new AlertFinding($"signal:stale:{signal.Key}", rule.Level, subject,
                    $"Nothing has been recorded for {signal.Key} since {signal.LastSeenUtc:yyyy-MM-dd HH:mm} UTC " +
                    $"({Ago(nowUtc - signal.LastSeenUtc)}).\n\n{rule.Hint}",
                    rule.RemindEvery));
            }
            else if (rule.IsFailing(signal, nowUtc))
            {
                findings.Add(new AlertFinding($"signal:failing:{signal.Key}", rule.Level, rule.FailingSubject!(signal),
                    $"{Describe(rule, signal, nowUtc)}\n\n{rule.Hint}", rule.RemindEvery));
            }
        }

        return Result.Ok<IReadOnlyList<AlertFinding>>(findings);
    }

    private static string Describe(SignalRule rule, HealthSignal signal, DateTime nowUtc)
    {
        if (rule.RecentWithin != null)
        {
            // An event signal: nothing records success, so the counter is every occurrence since the first.
            return $"Latest at {signal.LastFailureUtc:yyyy-MM-dd HH:mm} UTC: {signal.LastError}\n" +
                   $"{signal.ConsecutiveFailures} in total since {signal.FailingSinceUtc:yyyy-MM-dd HH:mm} UTC.";
        }

        var lastSuccess = signal.LastSuccessUtc is { } ok
            ? $"{ok:yyyy-MM-dd HH:mm} UTC ({Ago(nowUtc - ok)})"
            : "never recorded";
        return $"{signal.ConsecutiveFailures} failure(s) in a row since {signal.FailingSinceUtc:yyyy-MM-dd HH:mm} UTC.\n" +
               $"Last success: {lastSuccess}.\nLast error: {signal.LastError}";
    }

    internal static string Ago(TimeSpan span) => span.TotalDays >= 2
        ? $"{(int)span.TotalDays} days ago"
        : span.TotalHours >= 2 ? $"{(int)span.TotalHours} hours ago" : $"{Math.Max(0, (int)span.TotalMinutes)} minutes ago";
}
