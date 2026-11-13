using MediatR;
using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Persistence;

namespace Shuffull.Core.Features.Replacements.GetSongReplacementQueue;

/// <summary>No catch: a database fault stays an unhandled 500, as it was before this moved out of the controller.</summary>
public class GetSongReplacementQueueHandler(ShuffullContext context)
    : IRequestHandler<GetSongReplacementQueueQuery, Result<IReadOnlyList<SongReplacementDto>>>
{
    public async Task<Result<IReadOnlyList<SongReplacementDto>>> Handle(GetSongReplacementQueueQuery request, CancellationToken cancellationToken)
    {
        var rows = await context.SongReplacements
            .Where(r => r.Status == request.Status)
            .OrderBy(r => r.CreatedAt)
            .Select(r => new SongReplacementDto(
                r.SongReplacementId,
                r.SongId,
                r.Song.Name,
                r.Song.SongArtists.Select(sa => sa.Artist.Name).ToList(),
                r.OriginalExternalSongId,
                r.Note,
                r.CreatedAt))
            .ToListAsync(cancellationToken);

        return Result.Ok<IReadOnlyList<SongReplacementDto>>(rows);
    }
}
