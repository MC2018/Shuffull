using MediatR;
using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Persistence;

namespace Shuffull.Core.Features.YoutubeRatings.GetYoutubeRatingQueue;

/// <summary>
/// No catch: a database fault stays an unhandled 500, as it was before this moved out of the controller.
/// <c>ResultErrors.FromException</c> would answer some faults with a 400 or 409 instead.
/// </summary>
public class GetYoutubeRatingQueueHandler(ShuffullContext context)
    : IRequestHandler<GetYoutubeRatingQueueQuery, Result<IReadOnlyList<YoutubeRatingDto>>>
{
    public async Task<Result<IReadOnlyList<YoutubeRatingDto>>> Handle(GetYoutubeRatingQueueQuery request, CancellationToken cancellationToken)
    {
        var rows = await context.YoutubeRatingRequests
            .Where(r => r.Status == request.Status)
            .OrderBy(r => r.UpdatedAt)
            .Select(r => new { r.YoutubeRatingRequestId, r.SongId, r.VideoId, r.Rating })
            .ToListAsync(cancellationToken);

        return Result.Ok<IReadOnlyList<YoutubeRatingDto>>(rows
            .Select(r => new YoutubeRatingDto(r.YoutubeRatingRequestId, r.SongId, r.VideoId, YoutubeRatingApiValues.ToApi(r.Rating)))
            .ToList());
    }
}
