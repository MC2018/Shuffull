using MediatR;
using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Persistence;
using Shuffull.Metadata.Contracts;

namespace Shuffull.Core.Features.SongRequests.GetSongRequestQueue;

public class GetSongRequestQueueHandler(ShuffullContext context)
    : IRequestHandler<GetSongRequestQueueQuery, Result<IReadOnlyList<PendingSongRequestDto>>>
{
    /// <summary>Rows per producer poll; the rest come on the next poll.</summary>
    public const int Limit = 200;

    public async Task<Result<IReadOnlyList<PendingSongRequestDto>>> Handle(GetSongRequestQueueQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var rows = await context.SongRequests
                .AsNoTracking()
                .Where(r => r.Status == request.Status)
                .OrderBy(r => r.CreatedAt)
                .ThenBy(r => r.SongRequestId)
                .Take(Limit)
                .ToListAsync(cancellationToken);

            return Result.Ok<IReadOnlyList<PendingSongRequestDto>>(rows
                .Select(r => new PendingSongRequestDto(r.SongRequestId, r.UserId, r.ExternalSongId, r.Title,
                    SongRequestMapping.ArtistsOf(r), r.DurationSeconds, r.ThumbnailUrl, r.TargetPlaylistName))
                .ToList());
        }
        catch (Exception ex)
        {
            return Result.Error<IReadOnlyList<PendingSongRequestDto>>(ResultErrors.FromException(ex, $"Could not read the song request queue: {ex.Message}"));
        }
    }
}
