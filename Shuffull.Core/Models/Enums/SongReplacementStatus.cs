namespace Shuffull.Core.Models.Enums;

/// <summary>Lifecycle of a <see cref="Database.SongReplacement"/> request.</summary>
public enum SongReplacementStatus
{
    /// <summary>Flagged by a user; waiting for the producer to pick it up.</summary>
    Pending = 0,
    /// <summary>Claimed by the producer (a human is re-sourcing a better version).</summary>
    InProgress = 1,
    /// <summary>A replacement import landed; the song was replaced in place.</summary>
    Completed = 2,
}
