using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Persistence;

namespace Shuffull.Core.Features.Songs.GetDeletedSongs;

public class GetDeletedSongsHandler(ShuffullContext context)
    : IRequestHandler<GetDeletedSongsQuery, Result<DeletedSongsPage>>
{
    private const int DefaultLimit = 500;
    private const int MaxLimit = 1000;

    public async Task<Result<DeletedSongsPage>> Handle(GetDeletedSongsQuery request, CancellationToken cancellationToken)
    {
        var limit = request.Limit <= 0 ? DefaultLimit : Math.Min(request.Limit, MaxLimit);

        var query = context.SongTombstones
            .AsNoTracking()
            .Where(t => t.DeletedByUserId == request.UserId);

        if (!string.IsNullOrEmpty(request.Before))
        {
            if (!TryParseCursor(request.Before, out var deletedAt, out var songId))
            {
                return Result.Error<DeletedSongsPage>(new KindedError(ErrorKind.Invalid, "Invalid 'before' cursor."));
            }

            // Keyset on (DeletedAt, SongId), newest first. One purge stamps every song it removes with the same
            // DeletedAt, so the id breaks the tie; without it a page boundary inside one purge would skip or repeat.
            query = query.Where(t => t.DeletedAt < deletedAt
                || (t.DeletedAt == deletedAt && string.Compare(t.SongId, songId) < 0));
        }

        // One extra row tells us whether there is a next page without a separate COUNT.
        var rows = await query
            .OrderByDescending(t => t.DeletedAt)
            .ThenByDescending(t => t.SongId)
            .Take(limit + 1)
            .Select(t => new DeletedSong(t.SongId, t.Name, t.ArtistNames, t.ExternalSongId, t.PlaylistName, t.DeletedAt))
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > limit;
        var songs = rows.Take(limit).ToList();
        return Result.Ok(new DeletedSongsPage(songs, hasMore ? FormatCursor(songs[^1]) : null));
    }

    private static string FormatCursor(DeletedSong song) =>
        $"{song.DeletedAt.Ticks.ToString(CultureInfo.InvariantCulture)}_{song.SongId}";

    private static bool TryParseCursor(string cursor, out DateTime deletedAt, out string songId)
    {
        deletedAt = default;
        songId = string.Empty;
        var split = cursor.IndexOf('_');
        if (split <= 0 || split == cursor.Length - 1
            || !long.TryParse(cursor.AsSpan(0, split), NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks > DateTime.MaxValue.Ticks)
        {
            return false;
        }

        deletedAt = new DateTime(ticks, DateTimeKind.Utc);
        songId = cursor[(split + 1)..];
        return true;
    }
}
