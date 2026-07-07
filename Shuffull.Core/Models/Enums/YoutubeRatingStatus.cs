namespace Shuffull.Core.Models.Enums;

/// <summary>Lifecycle of a <see cref="Database.YoutubeRatingRequest"/> (the like-parity queue row).</summary>
public enum YoutubeRatingStatus
{
    /// <summary>The desired rating changed in Shuffull; waiting for the producer to apply it on YouTube.</summary>
    Pending = 0,
    /// <summary>The producer applied the rating on YouTube.</summary>
    Completed = 1,
}
