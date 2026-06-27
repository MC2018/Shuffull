using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shuffull.Shared.Tools;
using Shuffull.Api.Tools.Authorization;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Persistence;

namespace Shuffull.Api.Controllers;

/// <summary>
/// Song-replacement queue. The app flags a poor-quality song here (authenticated); the producer pulls the
/// pending queue (shared-secret) to re-source a better version. Resolution happens automatically when a
/// replacement import lands (see SongImportService.ReplaceInDbAsync), which marks the request Completed.
/// </summary>
[ApiController]
[Route("api/v1/replacements")]
public class ReplacementsController : ControllerBase
{
    private readonly ShuffullContext _context;
    private readonly IConfiguration _configuration;

    public ReplacementsController(ShuffullContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    /// <summary>App: flag a song for replacement. Global per song — a no-op if one is already open.</summary>
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Flag([FromBody] FlagReplacementRequest request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.SongId))
        {
            return BadRequest("SongId is required.");
        }

        var song = await _context.Songs.FirstOrDefaultAsync(s => s.SongId == request.SongId, cancellationToken);
        if (song is null)
        {
            return NotFound("Song not found.");
        }

        var alreadyOpen = await _context.SongReplacements
            .AnyAsync(r => r.SongId == request.SongId && r.Status != SongReplacementStatus.Completed, cancellationToken);
        if (!alreadyOpen)
        {
            _context.SongReplacements.Add(new SongReplacement
            {
                SongReplacementId = IdGenerator.Generate(),
                SongId = request.SongId,
                Status = SongReplacementStatus.Pending,
                Note = request.Note,
                // Snapshot the source id now, before a replacement overwrites Song.ExternalSongId.
                OriginalExternalSongId = song.ExternalSongId,
                CreatedAt = DateTime.UtcNow,
            });
            await _context.SaveChangesAsync(cancellationToken);
        }

        return Ok();
    }

    /// <summary>Producer: pull the replacement queue at a given status (shared-secret guarded).</summary>
    [HttpGet]
    public async Task<IActionResult> GetQueue(
        [FromQuery] SongReplacementStatus status,
        [FromHeader(Name = "X-Import-Key")] string? importKey,
        CancellationToken cancellationToken)
    {
        var expectedKey = _configuration["Shuffull:Import:Key"];
        if (string.IsNullOrEmpty(expectedKey) || importKey != expectedKey)
        {
            return Unauthorized();
        }

        var rows = await _context.SongReplacements
            .Where(r => r.Status == status)
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

        return Ok(rows);
    }
}

public record FlagReplacementRequest(string SongId, string? Note);

public record SongReplacementDto(
    string SongReplacementId,
    string SongId,
    string Name,
    List<string> Artists,
    string? OriginalExternalSongId,
    string? Note,
    DateTime CreatedAt);
