using MediatR;
using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Persistence;

namespace Shuffull.Core.Features.SongRequests.CancelSongRequest;

/// <summary>
/// One conditional UPDATE, Pending to Cancelled, scoped to the user: a producer's Accepted that commits first wins and
/// the cancel is a conflict. Returns the request as it now is.
/// </summary>
public class CancelSongRequestHandler(ShuffullContext context, TimeProvider timeProvider)
    : IRequestHandler<CancelSongRequestCommand, Result<SongRequestDto>>
{
    public async Task<Result<SongRequestDto>> Handle(CancelSongRequestCommand request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        try
        {
            var cancelled = await context.SongRequests
                .Where(r => r.SongRequestId == request.SongRequestId && r.UserId == request.UserId
                            && r.Status == SongRequestStatus.Pending)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(r => r.Status, SongRequestStatus.Cancelled)
                    .SetProperty(r => r.UpdatedAt, now), cancellationToken);

            var row = await context.SongRequests.AsNoTracking()
                .FirstOrDefaultAsync(r => r.SongRequestId == request.SongRequestId && r.UserId == request.UserId, cancellationToken);
            if (row is null)
            {
                return Result.Error<SongRequestDto>(ResultErrors.NotFound("Song request not found."));
            }
            return cancelled > 0
                ? Result.Ok(SongRequestMapping.ToDto(row))
                : Result.Error<SongRequestDto>(ResultErrors.Conflict($"This request is {row.Status} and can no longer be cancelled."));
        }
        catch (Exception ex)
        {
            return Result.Error<SongRequestDto>(ResultErrors.FromException(ex, $"Could not cancel the song request: {ex.Message}"));
        }
    }
}
