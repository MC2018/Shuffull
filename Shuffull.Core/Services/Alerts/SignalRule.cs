using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Services.Alerts;

/// <summary>
/// When a <see cref="HealthSignal"/> is worth an email. A rule matches a signal key exactly, or every key under it
/// when <see cref="Key"/> ends in a colon (<c>probe:</c> matches <c>probe:funnel</c>). It fires on any of:
/// <list type="bullet">
/// <item><b>failing</b>: at least <see cref="MinFailures"/> failures in a row, the first of them at least
/// <see cref="FailingFor"/> ago;</item>
/// <item><b>recent</b> (when <see cref="RecentWithin"/> is set, instead of failing): a failure within that window.
/// For events that never record a success, such as a refused Keep;</item>
/// <item><b>stale</b>: nothing at all recorded for <see cref="StaleAfter"/>, i.e. the thing stopped happening.</item>
/// </list>
/// A rule with no <see cref="FailingSubject"/> only watches for staleness.
/// </summary>
public sealed record SignalRule(
    string Key,
    Func<HealthSignal, string>? FailingSubject,
    string Hint,
    int MinFailures = 1,
    TimeSpan? FailingFor = null,
    int Level = 2,
    TimeSpan? RemindEvery = null,
    TimeSpan? StaleAfter = null,
    Func<HealthSignal, string>? StaleSubject = null,
    TimeSpan? RecentWithin = null)
{
    public bool Matches(string signalKey) =>
        Key.EndsWith(':') ? signalKey.StartsWith(Key, StringComparison.Ordinal) : signalKey == Key;

    public bool IsFailing(HealthSignal signal, DateTime nowUtc)
    {
        if (FailingSubject == null)
        {
            return false;
        }

        if (RecentWithin is { } window)
        {
            return signal.LastFailureUtc is { } last && last >= nowUtc - window;
        }

        return signal.ConsecutiveFailures >= MinFailures
            && signal.FailingSinceUtc is { } since
            && nowUtc - since >= (FailingFor ?? TimeSpan.Zero);
    }

    public bool IsStale(HealthSignal signal, DateTime nowUtc) =>
        StaleAfter is { } after && nowUtc - signal.LastSeenUtc >= after;

    /// <summary>The part of the key after the rule's prefix: the probe or poll name.</summary>
    public static string Suffix(HealthSignal signal) =>
        signal.Key.Contains(':') ? signal.Key[(signal.Key.IndexOf(':') + 1)..] : signal.Key;
}
