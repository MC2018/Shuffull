using Shuffull.Core.Models.Enums;

namespace Shuffull.Core.Features.YoutubeRatings;

/// <summary>The YouTube Data API <c>videos.rate</c> values, so the funnel can pass them straight through.</summary>
public static class YoutubeRatingApiValues
{
    public static string ToApi(YoutubeRating rating) => rating switch
    {
        YoutubeRating.Like => "like",
        YoutubeRating.Dislike => "dislike",
        _ => "none",
    };

    /// <summary>Trimmed and case-folded; anything unrecognised reads as <see cref="YoutubeRating.None"/>.</summary>
    public static YoutubeRating FromApi(string? rating) => rating?.Trim().ToLowerInvariant() switch
    {
        "like" => YoutubeRating.Like,
        "dislike" => YoutubeRating.Dislike,
        _ => YoutubeRating.None,
    };
}
