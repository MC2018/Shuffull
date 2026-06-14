namespace Shuffull.Api.Models.YouTube;

public class YoutubeVideoFeatures
{
    public string VideoId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string ChannelId { get; init; } = string.Empty;
    public string ChannelTitle { get; init; } = string.Empty;
    public int? CategoryId { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public int DurationSeconds { get; init; }
    public bool LicensedContent { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<string> Hashtags { get; init; } = [];
    public IReadOnlyList<string> TopicCategories { get; init; } = [];
}
