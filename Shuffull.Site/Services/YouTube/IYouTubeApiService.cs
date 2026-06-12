using Nut.Results;
using Shuffull.Site.Models.YouTube;

namespace Shuffull.Site.Services.YouTube;

public interface IYouTubeApiService
{
    Task<Result<IReadOnlyList<YoutubeVideoFeatures>>> GetVideoFeaturesAsync(IEnumerable<string> videoIds, CancellationToken cancellationToken = default);
}
