namespace Shuffull.Core.Models.Enums;

/// <summary>Lifecycle of a <see cref="Database.YoutubeRatingRequest"/> (the like-parity queue row).</summary>
public enum YoutubeRatingStatus
{
    /// <summary>The desired rating changed in Shuffull; waiting for the producer to apply it on YouTube.</summary>
    Pending = 0,
    /// <summary>The producer applied the rating on YouTube.</summary>
    Completed = 1,
    /// <summary>
    /// YouTube will never accept this rating — the video's owner has disabled ratings on it. Terminal, so the
    /// producer stops asking.
    ///
    /// <para>This exists because retrying costs real quota: <c>videos.rate</c> is charged 50 units, and two
    /// permanently-unratable rows retried on a 10-minute cycle burn ~14k units/day against a 10k/day
    /// allowance — enough to starve the ingest of the quota it shares. A TRANSIENT failure (out of quota,
    /// network, an expired token) must NOT land here; it stays <see cref="Pending"/> and is retried after the
    /// daily reset.</para>
    /// </summary>
    Skipped = 2,
}
