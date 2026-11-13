using MediatR;
using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Persistence;

namespace Shuffull.Core.Features.Songs.UpdateSongLyrics;

/// <summary>
/// Attaches lyrics to the song imported from a given YouTube video id, and bumps <c>Song.Version</c> so every client
/// picks them up on its next incremental sync.
///
/// <para>Lyrics used to reach the site only inside a full song import, so anything the producer resolved AFTER export
/// (its backfill loop, or a re-check once a newly released track appears on LRCLIB) was never delivered. This is that
/// delivery step.</para>
///
/// <para>No catch: a database fault stays an unhandled 500, as it was before this moved out of the controller.</para>
/// </summary>
public class UpdateSongLyricsHandler(ShuffullContext context, TimeProvider timeProvider)
    : IRequestHandler<UpdateSongLyricsCommand, Result<UpdateSongLyricsResponse>>
{
    public async Task<Result<UpdateSongLyricsResponse>> Handle(UpdateSongLyricsCommand request, CancellationToken cancellationToken)
    {
        var song = await context.Songs
            .FirstOrDefaultAsync(s => s.ExternalSongId == request.ExternalSongId, cancellationToken);
        if (song is null)
        {
            return Result.Error<UpdateSongLyricsResponse>(
                ResultErrors.NotFound($"No song found for external id '{request.ExternalSongId}'."));
        }

        // A curator edit is authoritative over an automated delivery: if someone has locked this song's
        // metadata by hand, the producer must not overwrite it.
        if (song.MetadataLocked)
        {
            return Result.Ok(UpdateSongLyricsResponse.Skipped("metadata locked"));
        }

        song.SyncedLyrics = string.IsNullOrWhiteSpace(request.SyncedLyrics) ? null : request.SyncedLyrics;
        song.PlainLyrics = string.IsNullOrWhiteSpace(request.PlainLyrics) ? null : request.PlainLyrics;
        song.LyricsInstrumental = request.Instrumental;
        song.LyricsSource = string.IsNullOrWhiteSpace(request.Source) ? null : request.Source.Trim();
        // Drives the app's incremental song sync.
        song.Version = timeProvider.GetUtcNow().UtcDateTime;

        await context.SaveChangesAsync(cancellationToken);

        return Result.Ok(UpdateSongLyricsResponse.Stored(song.SongId));
    }
}
