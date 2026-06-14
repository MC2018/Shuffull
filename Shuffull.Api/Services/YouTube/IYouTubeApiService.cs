using Nut.Results;
using Shuffull.Api.Models.YouTube;

namespace Shuffull.Api.Services.YouTube;

public interface IYouTubeApiService
{
    Task<Result<IReadOnlyList<YoutubeVideoFeatures>>> GetVideoFeaturesAsync(IEnumerable<string> videoIds, CancellationToken cancellationToken = default);
}
