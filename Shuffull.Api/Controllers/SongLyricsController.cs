using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shuffull.Api.Commands.Songs.UpdateSongLyrics;
using Shuffull.Core.Persistence;

namespace Shuffull.Api.Controllers;

/// <summary>
/// Producer-facing lyrics delivery.
///
/// Lyrics used to reach the site only inside a full song import, which meant anything the producer resolved
/// AFTER export — its backfill loop, or a re-check once a newly-released track finally appears on LRCLIB —
/// was recorded producer-side and never delivered. The songs stayed lyric-less forever unless the audio was
/// re-imported, which is an absurd price for a text update. This is the missing delivery step.
///
/// Shared-secret guarded like the other producer endpoints.
/// </summary>
[ApiController]
[Route("api/v1/song-lyrics")]
public class SongLyricsController : ControllerBase
{
    private readonly ShuffullContext _context;
    private readonly IConfiguration _configuration;

    public SongLyricsController(ShuffullContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    private bool IsAuthorized(string? importKey)
    {
        var expectedKey = _configuration["Shuffull:Import:Key"];
        return !string.IsNullOrEmpty(expectedKey) && importKey == expectedKey;
    }

    /// <summary>
    /// Attaches lyrics to the song imported from a given YouTube video id. Bumps <c>Song.Version</c> so every
    /// client picks them up on its next incremental sync — no app change needed.
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> UpdateLyrics(
        [FromBody] UpdateSongLyricsRequest request,
        [FromHeader(Name = "X-Import-Key")] string? importKey,
        CancellationToken cancellationToken)
    {
        if (!IsAuthorized(importKey))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.ExternalSongId))
        {
            return BadRequest(new { error = "ExternalSongId is required." });
        }

        // Refuse an all-empty payload. Otherwise a producer-side bug (or a re-check that found nothing)
        // could blank lyrics the site already holds — a silent regression that is invisible until someone
        // opens the lyrics panel.
        var hasContent = !string.IsNullOrWhiteSpace(request.SyncedLyrics)
            || !string.IsNullOrWhiteSpace(request.PlainLyrics)
            || request.Instrumental;
        if (!hasContent)
        {
            return BadRequest(new { error = "No lyrics content supplied; nothing to update." });
        }

        var song = await _context.Songs
            .FirstOrDefaultAsync(s => s.ExternalSongId == request.ExternalSongId, cancellationToken);

        if (song is null)
        {
            return NotFound(new { error = $"No song found for external id '{request.ExternalSongId}'." });
        }

        // A curator edit is authoritative over an automated delivery: if someone has locked this song's
        // metadata by hand, the producer must not overwrite it.
        if (song.MetadataLocked)
        {
            return Ok(new { updated = false, reason = "metadata locked" });
        }

        song.SyncedLyrics = string.IsNullOrWhiteSpace(request.SyncedLyrics) ? null : request.SyncedLyrics;
        song.PlainLyrics = string.IsNullOrWhiteSpace(request.PlainLyrics) ? null : request.PlainLyrics;
        song.LyricsInstrumental = request.Instrumental;
        song.LyricsSource = string.IsNullOrWhiteSpace(request.Source) ? null : request.Source.Trim();
        // Drives the app's incremental song sync.
        song.Version = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return Ok(new { updated = true, songId = song.SongId });
    }
}
