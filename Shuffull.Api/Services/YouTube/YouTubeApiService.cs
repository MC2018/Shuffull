using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using Nut.Results;
using Shuffull.Api.Configuration;
using Shuffull.Api.Models.YouTube;
using System.Text.RegularExpressions;
using System.Xml;

namespace Shuffull.Api.Services.YouTube;

public partial class YouTubeApiService : IYouTubeApiService
{
    private readonly ILogger<YouTubeApiService> _logger;
    private readonly YouTubeApiConfiguration _config;
    private readonly YouTubeService _service;
    private const int MaxVideosPerRequest = 50;

    public YouTubeApiService(IConfiguration configuration, ILogger<YouTubeApiService> logger)
    {
        _logger = logger;
        _config = configuration.GetSection(YouTubeApiConfiguration.YouTubeApiConfigurationSection).Get<YouTubeApiConfiguration>()
            ?? throw new ArgumentNullException(nameof(YouTubeApiConfiguration), "YouTubeApiConfiguration is not configured properly.");

        if (string.IsNullOrEmpty(_config.ApiKey))
        {
            throw new InvalidOperationException("YouTube API key is not set in the configuration.");
        }

        _service = new YouTubeService(new BaseClientService.Initializer
        {
            ApiKey = _config.ApiKey,
            ApplicationName = _config.ApplicationName
        });

        _logger.LogInformation("YouTubeApiService initialized successfully.");
    }

    public async Task<Result<IReadOnlyList<YoutubeVideoFeatures>>> GetVideoFeaturesAsync(IEnumerable<string> videoIds, CancellationToken cancellationToken = default)
    {
        try
        {
            var distinctIds = videoIds.Distinct().ToArray();
            if (distinctIds.Length == 0)
            {
                return Result.Ok<IReadOnlyList<YoutubeVideoFeatures>>([]);
            }

            var result = new List<YoutubeVideoFeatures>(distinctIds.Length);

            // Process videos in batches of 50 (YouTube API limit)
            foreach (var batch in distinctIds.Chunk(MaxVideosPerRequest))
            {
                var batchResult = await GetVideoBatchAsync(batch, cancellationToken);
                if (batchResult.IsError)
                {
                    return Result.Error<IReadOnlyList<YoutubeVideoFeatures>>(batchResult.GetError());
                }

                result.AddRange(batchResult.Get());
            }

            return Result.Ok<IReadOnlyList<YoutubeVideoFeatures>>(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching video features");
            return Result.Error<IReadOnlyList<YoutubeVideoFeatures>>(ex.Message);
        }
    }

    private async Task<Result<List<YoutubeVideoFeatures>>> GetVideoBatchAsync(IEnumerable<string> videoIds, CancellationToken cancellationToken)
    {
        try
        {
            var request = _service.Videos.List("snippet,contentDetails,topicDetails");
            request.Id = string.Join(",", videoIds);

            var response = await request.ExecuteAsync(cancellationToken);
            var features = new List<YoutubeVideoFeatures>(response.Items.Count);

            foreach (var video in response.Items)
            {
                var feature = MapVideoToFeatures(video);
                features.Add(feature);
            }

            return Result.Ok(features);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching video batch: {VideoIds}", string.Join(", ", videoIds));
            return Result.Error<List<YoutubeVideoFeatures>>(ex.Message);
        }
    }

    private YoutubeVideoFeatures MapVideoToFeatures(Video video)
    {
        var snippet = video.Snippet;
        var contentDetails = video.ContentDetails;
        var topicDetails = video.TopicDetails;

        // Parse ISO 8601 duration (e.g., PT3M33S)
        var duration = TimeSpan.Zero;
        try
        {
            duration = XmlConvert.ToTimeSpan(contentDetails.Duration);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse duration for video {VideoId}: {Duration}", video.Id, contentDetails.Duration);
        }

        // Extract hashtags from description
        var hashtags = ExtractHashtags(snippet?.Description ?? string.Empty);

        // Parse category ID
        int? categoryId = null;
        if (int.TryParse(snippet?.CategoryId, out var parsedCategoryId))
        {
            categoryId = parsedCategoryId;
        }

        return new YoutubeVideoFeatures
        {
            VideoId = video.Id,
            Title = snippet?.Title ?? string.Empty,
            ChannelId = snippet?.ChannelId ?? string.Empty,
            ChannelTitle = snippet?.ChannelTitle ?? string.Empty,
            CategoryId = categoryId,
            PublishedAt = snippet?.PublishedAtDateTimeOffset,
            DurationSeconds = (int)duration.TotalSeconds,
            LicensedContent = contentDetails?.LicensedContent ?? false,
            Tags = (IReadOnlyList<string>?)snippet?.Tags ?? [],
            Hashtags = hashtags,
            TopicCategories = (IReadOnlyList<string>?)topicDetails?.TopicCategories ?? []
        };
    }

    private static IReadOnlyList<string> ExtractHashtags(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return [];
        }

        var matches = HashtagRegex().Matches(description);
        return matches
            .Select(m => "#" + m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToArray();
    }

    [GeneratedRegex(@"(?<=\s|^)#([\p{L}\p{N}_]+)")]
    private static partial Regex HashtagRegex();
}

public static class YouTubeApiServiceExtensions
{
    public static IServiceCollection AddYouTubeApiService(this IServiceCollection services)
    {
        services.AddSingleton<IYouTubeApiService, YouTubeApiService>();
        return services;
    }
}
