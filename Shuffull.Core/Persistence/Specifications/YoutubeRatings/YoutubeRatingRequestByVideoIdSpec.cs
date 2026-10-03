using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence.Specifications;

namespace Shuffull.Core.Persistence.Specifications.YoutubeRatings;

/// <summary>
/// Loads the (at most one) rating request for a YouTube video id. The like-parity enqueue keeps a single row
/// per video, updating it in place so successive rating changes coalesce into one pending call.
/// </summary>
public class YoutubeRatingRequestByVideoIdSpec : BaseSpecification<YoutubeRatingRequest>
{
    private readonly string _videoId;

    public YoutubeRatingRequestByVideoIdSpec(string videoId)
    {
        _videoId = videoId;
    }

    protected override IQueryable<YoutubeRatingRequest> BuildQuery(IQueryable<YoutubeRatingRequest> query)
        => query.Where(r => r.VideoId == _videoId);
}
