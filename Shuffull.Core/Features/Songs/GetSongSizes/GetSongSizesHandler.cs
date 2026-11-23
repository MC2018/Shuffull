using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Persistence;
using Shuffull.Core.Services;

namespace Shuffull.Core.Features.Songs.GetSongSizes;

/// <summary>
/// Reads the caller's library from the database, then stats each song's files (<see cref="ISongMediaSizes"/>). Read
/// only: nothing is written, so no <c>Version</c> moves. A missing audio file drops that one song from the list, and one
/// warning per call names how many were dropped: a library song with no audio can't play or download, so it is broken
/// data worth seeing, and a whole library dropping at once points at the music folder rather than the songs.
/// </summary>
public class GetSongSizesHandler(ShuffullContext context, ISongMediaSizes mediaSizes, ILogger<GetSongSizesHandler> logger)
    : IRequestHandler<GetSongSizesQuery, Result<IReadOnlyList<SongSize>>>
{
    public async Task<Result<IReadOnlyList<SongSize>>> Handle(GetSongSizesQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var songs = await context.UserSongs
                .AsNoTracking()
                .Where(us => us.UserId == request.UserId)
                .OrderBy(us => us.SongId)
                .Select(us => new { us.SongId, us.Song.FileHash, us.Song.FileExtension })
                .ToListAsync(cancellationToken);

            var sizes = new List<SongSize>(songs.Count);
            var missing = new List<string>();
            foreach (var song in songs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var size = mediaSizes.GetSongMediaSize(song.FileHash, song.FileExtension);
                if (size is not null)
                {
                    sizes.Add(new SongSize(song.SongId, size.AudioBytes, size.ArtBytes));
                }
                else
                {
                    missing.Add(song.SongId);
                }
            }

            if (missing.Count > 0)
            {
                logger.LogWarning(
                    "Song sizes for user {UserId}: left out {Missing} of {Total} library songs whose audio file is missing or unreadable (first: {SongIds})",
                    request.UserId, missing.Count, songs.Count, string.Join(", ", missing.Take(5)));
            }

            return Result.Ok<IReadOnlyList<SongSize>>(sizes);
        }
        catch (Exception ex)
        {
            return Result.Error<IReadOnlyList<SongSize>>(ResultErrors.FromException(ex, $"Could not load song sizes. {ex.Message}"));
        }
    }
}
