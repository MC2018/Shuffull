namespace Shuffull.Core.Models.Enums;

/// <summary>
/// The rating to apply to a YouTube video, mirroring the YouTube Data API's <c>videos.rate</c> values
/// (<c>none</c>/<c>like</c>/<c>dislike</c>). A Shuffull <see cref="LikeStatus"/> maps onto this: Like/Love -&gt;
/// <see cref="Like"/>, Dislike -&gt; <see cref="Dislike"/>, Neutral -&gt; <see cref="None"/> (clears the rating).
/// </summary>
public enum YoutubeRating
{
    /// <summary>No rating — clears any existing like/dislike on YouTube.</summary>
    None = 0,
    Like = 1,
    Dislike = 2,
}
