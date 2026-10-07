using MediatR;
using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Persistence;

namespace Shuffull.Core.Features.Songs.GetLibrarySongs;

public class GetLibrarySongsHandler(ShuffullContext context)
    : IRequestHandler<GetLibrarySongsQuery, Result<LibrarySongsPage>>
{
    private const int DefaultLimit = 500;
    private const int MaxLimit = 1000;

    public async Task<Result<LibrarySongsPage>> Handle(GetLibrarySongsQuery request, CancellationToken cancellationToken)
    {
        var limit = request.Limit <= 0 ? DefaultLimit : Math.Min(request.Limit, MaxLimit);

        var query = context.Songs.AsNoTracking();
        if (!string.IsNullOrEmpty(request.AfterSongId))
        {
            // Ordinal keyset on the primary key: stable under concurrent inserts (a new song lands on a later page
            // or the next pass) and never skips or repeats a row the way an offset would.
            query = query.Where(s => string.Compare(s.SongId, request.AfterSongId) > 0);
        }

        // One extra row tells us whether there is a next page without a separate COUNT.
        var rows = await query
            .OrderBy(s => s.SongId)
            .Take(limit + 1)
            .Select(s => new
            {
                s.SongId,
                s.ExternalSongId,
                s.Name,
                Artists = s.SongArtists.Select(sa => sa.Artist.Name).ToList(),
                s.FileHash,
                s.FileExtension,
            })
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > limit;
        var songs = rows
            .Take(limit)
            .Select(r => new LibrarySong(r.SongId, r.ExternalSongId, r.Name, r.Artists, r.FileHash, r.FileExtension))
            .ToList();

        return Result.Ok(new LibrarySongsPage(songs, hasMore ? songs[^1].SongId : null));
    }
}
