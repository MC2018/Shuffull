using System.ComponentModel.DataAnnotations;

namespace Shuffull.Core.Models.Database;

/// <summary>
/// What the alert monitor last told the user about one alert key, so it emails once when a problem starts, again
/// only when it gets worse or a reminder is due, and once more when it clears. Without this row a restart would
/// re-send every open alert. A row with <see cref="Level"/> 0 is a resolved alert, kept for its history.
/// </summary>
public class AlertState
{
    public const int MaxKeyLength = 160;
    public const int MaxSubjectLength = 300;

    [Key, MaxLength(MaxKeyLength)]
    public string Key { get; private set; } = null!;

    /// <summary>0 when clear; otherwise the level last emailed (higher is worse).</summary>
    public int Level { get; private set; }

    [MaxLength(MaxSubjectLength)]
    public string Subject { get; private set; } = string.Empty;

    public DateTime? FiringSinceUtc { get; private set; }

    public DateTime? LastNotifiedUtc { get; private set; }

    /// <summary>When the check first stopped reporting this alert. It resolves only after staying clear a while, so
    /// a flapping check doesn't send a resolve and a new alert every few minutes.</summary>
    public DateTime? ClearSinceUtc { get; private set; }

    public DateTime? ResolvedAtUtc { get; private set; }

    private AlertState()
    {
    }

    public static AlertState Create(string key) => new() { Key = key };

    public bool IsFiring => Level > 0;

    /// <summary>The alert started, got worse, or was reminded: the email for it went out.</summary>
    public void Notified(int level, string subject, DateTime nowUtc)
    {
        if (!IsFiring)
        {
            FiringSinceUtc = nowUtc;
        }

        Level = level;
        Subject = Truncate(subject);
        LastNotifiedUtc = nowUtc;
        ClearSinceUtc = null;
        ResolvedAtUtc = null;
    }

    /// <summary>Still firing, at a level that needs no email (unchanged, or lower than what was sent).</summary>
    public void StillFiring(int level, string subject)
    {
        Level = level;
        Subject = Truncate(subject);
        ClearSinceUtc = null;
    }

    public void StartedClearing(DateTime nowUtc) => ClearSinceUtc ??= nowUtc;

    /// <summary>The resolve email went out.</summary>
    public void Resolved(DateTime nowUtc)
    {
        Level = 0;
        FiringSinceUtc = null;
        ClearSinceUtc = null;
        ResolvedAtUtc = nowUtc;
        LastNotifiedUtc = nowUtc;
    }

    private static string Truncate(string value) => value.Length <= MaxSubjectLength ? value : value[..MaxSubjectLength];
}
