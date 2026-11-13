namespace Shuffull.Core.Features.YoutubeRatings;

/// <summary>The stable ack outcome strings on the wire.</summary>
public static class YoutubeRatingOutcomes
{
    /// <summary>The rating was pushed to YouTube successfully.</summary>
    public const string Applied = "applied";
    /// <summary>YouTube will never accept it (ratings disabled on the video). Terminal — stop retrying.</summary>
    public const string Unratable = "unratable";
}
