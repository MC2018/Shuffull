namespace Shuffull.Core.Models.Enums;

/// <summary>
/// Lifecycle of a <see cref="Database.SongRequest"/>. The producer's progress states (1-4) carry the same numbers as
/// <see cref="Shuffull.Metadata.Enums.SongRequestAckStatus"/>, so an ack maps across by value. The rest are Shuffull's
/// own: the row is created <see cref="Pending"/>, an import (or the instant path) sets <see cref="Completed"/>, and the
/// user sets <see cref="Cancelled"/>. Goes over the wire as its int.
/// </summary>
public enum SongRequestStatus
{
    /// <summary>Waiting for the producer to pick it up.</summary>
    Pending = 0,
    /// <summary>The producer queued it for download.</summary>
    Accepted = 1,
    /// <summary>A download attempt is under way.</summary>
    Downloading = 2,
    /// <summary>An attempt failed and the producer is trying again.</summary>
    Retrying = 3,
    /// <summary>
    /// The producer stopped reporting progress. Still open: a late import can complete it, and the user can request
    /// the song again, which puts it back to <see cref="Pending"/>.
    /// </summary>
    Failed = 4,
    /// <summary>The song is in the user's library and their requests playlist. Terminal.</summary>
    Completed = 5,
    /// <summary>The user withdrew it while it was still <see cref="Pending"/>. Terminal.</summary>
    Cancelled = 6,
}
