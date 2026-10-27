using System.ComponentModel.DataAnnotations;

namespace Shuffull.Core.Models.Database;

/// <summary>
/// The last known outcome of one recurring thing the site does or depends on: a media sweep pass, the funnel polling
/// for work, a refused Keep. Code writes it through <c>IHealthSignals</c>, and the alert monitor reads it to notice
/// work that keeps failing or has stopped happening (Shuffull#75).
///
/// <para>The API logs only to files inside its container, so nobody sees them, and several of the failures watched
/// here don't log at all (a refused Keep is only returned to the app). This row is the durable fact the monitor can
/// query instead. Derived state, safe to wipe: a signal that should keep arriving (a funnel poll) is treated as last
/// seen when the API started, so a wiped or never-written row still goes stale and alerts.</para>
/// </summary>
public class HealthSignal
{
    public const int MaxKeyLength = 128;
    public const int MaxErrorLength = 1000;

    /// <summary>What this is about, e.g. <c>media-sweep</c>, <c>funnel-poll:pending-tags</c>.</summary>
    [Key, MaxLength(MaxKeyLength)]
    public string Key { get; private set; } = null!;

    public DateTime? LastSuccessUtc { get; private set; }

    public DateTime? LastFailureUtc { get; private set; }

    /// <summary>The first failure of the current run of failures; null once something succeeds.</summary>
    public DateTime? FailingSinceUtc { get; private set; }

    /// <summary>Failures since the last success.</summary>
    public int ConsecutiveFailures { get; private set; }

    [MaxLength(MaxErrorLength)]
    public string? LastError { get; private set; }

    /// <summary>The latest success or failure: when this thing last ran at all.</summary>
    public DateTime LastSeenUtc { get; private set; }

    private HealthSignal()
    {
    }

    /// <summary>The stored form of a key: trimmed and cut to <see cref="MaxKeyLength"/>. Look keys up by this too.</summary>
    public static string NormalizeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("A health signal needs a key.", nameof(key));

        var trimmed = key.Trim();
        return trimmed.Length <= MaxKeyLength ? trimmed : trimmed[..MaxKeyLength];
    }

    public static HealthSignal Create(string key) => new() { Key = NormalizeKey(key) };

    public void RecordSuccess(DateTime nowUtc)
    {
        LastSuccessUtc = nowUtc;
        LastSeenUtc = nowUtc;
        FailingSinceUtc = null;
        ConsecutiveFailures = 0;
    }

    public void RecordFailure(string error, DateTime nowUtc)
    {
        LastFailureUtc = nowUtc;
        LastSeenUtc = nowUtc;
        FailingSinceUtc ??= nowUtc;
        ConsecutiveFailures++;
        var message = string.IsNullOrWhiteSpace(error) ? "(no error message)" : error.Trim();
        LastError = message.Length <= MaxErrorLength ? message : message[..MaxErrorLength];
    }
}
