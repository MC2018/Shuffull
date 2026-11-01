using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shuffull.Api.Commands.SongRequests;
using Shuffull.Api.Tools;
using Shuffull.Api.Tools.Authorization;
using Shuffull.Core.Features.SongRequests;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Persistence;
using Shuffull.Metadata.Contracts;
using Shuffull.Metadata.Enums;
using Shuffull.Shared.Tools;

namespace Shuffull.Api.Controllers;

/// <summary>
/// Song requests (Shuffull#57): a user asks for a song the library doesn't have, and the producer fetches it. The same
/// durable row + poll + ack shape as <see cref="YoutubeRatingsController"/>.
/// <list type="bullet">
/// <item>The app (signed in, any user) creates, lists and cancels its own requests. A request for a song the library
/// already has completes at once: the song is linked to the user and their weekly requests playlist, no producer.</item>
/// <item>The producer (shared secret) polls <c>?status=Pending</c>, acks its progress conditionally, and completes a
/// request by importing the song with <c>SongImportDetails.SongRequestId</c> (<see cref="SongRequestCompletion"/>).</item>
/// </list>
/// The request row is the user's intent: it is written in its own transaction, before and regardless of any download.
/// </summary>
[ApiController]
[Route("api/v1/song-requests")]
public class SongRequestsController : ControllerBase
{
    /// <summary>How many of the user's requests the app's list returns, newest first.</summary>
    public const int RecentLimit = 50;
    /// <summary>Open requests one user may have at once, so one account can't queue the producer without bound.</summary>
    public const int MaxOpenPerUser = 50;
    /// <summary>Rows per producer poll, oldest first; the rest come on the next poll.</summary>
    public const int PollLimit = 200;
    public const int MaxAckBatch = 200;
    public const int MaxArtists = 20;
    public const int MaxArtistNameLength = 200;

    private readonly ShuffullContext _context;
    private readonly TimeProvider _time;

    public SongRequestsController(ShuffullContext context, TimeProvider time)
    {
        _context = context;
        _time = time;
    }

    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    /// <summary>
    /// App: ask for a song by its source id.
    /// <list type="bullet">
    /// <item>The library has it: the request is Completed now, and the song is linked to the user and this week's
    /// requests playlist in the same transaction.</item>
    /// <item>The user has a Failed request for it: that request goes back to Pending (this is the app's retry).</item>
    /// <item>The user has another open request for it: 409, with that request as the body.</item>
    /// <item>Otherwise a new Pending request for the producer.</item>
    /// </list>
    /// </summary>
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Create([FromBody] CreateSongRequestRequest request, CancellationToken cancellationToken)
    {
        if (HttpContext.Items["User"] is not User user)
        {
            return Unauthorized();
        }

        var invalid = Validate(request);
        if (invalid is not null)
        {
            return BadRequest(new { message = invalid });
        }

        var externalSongId = request.ExternalSongId.Trim();
        var artists = request.Artists!.Select(a => a.Trim()).ToList();
        var now = UtcNow;
        var open = SongRequestRules.Open;

        var existing = await _context.SongRequests
            .FirstOrDefaultAsync(r => r.UserId == user.UserId && r.ExternalSongId == externalSongId && open.Contains(r.Status),
                cancellationToken);
        var song = await _context.Songs
            .Where(s => s.ExternalSongId == externalSongId)
            .OrderBy(s => s.SongId)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null && song is null)
        {
            if (existing.Status != SongRequestStatus.Failed)
            {
                return Conflict(ToDto(existing));
            }

            // Retry: the same row goes back on the queue. The ack rules let a producer still holding it report again.
            existing.Status = SongRequestStatus.Pending;
            existing.FailureReason = null;
            existing.UpdatedAt = now;
            return await SaveAsync(existing, cancellationToken);
        }

        if (existing is null && song is null)
        {
            var openCount = await _context.SongRequests
                .CountAsync(r => r.UserId == user.UserId && open.Contains(r.Status), cancellationToken);
            if (openCount >= MaxOpenPerUser)
            {
                return StatusCode(StatusCodes.Status429TooManyRequests,
                    new { message = $"You already have {openCount} open song requests; wait for some to finish." });
            }
        }

        var row = existing ?? new SongRequest
        {
            SongRequestId = IdGenerator.Generate(),
            UserId = user.UserId,
            ExternalSongId = externalSongId,
            Title = request.Title.Trim(),
            ArtistsJson = JsonSerializer.Serialize(artists),
            DurationSeconds = request.DurationSeconds,
            ThumbnailUrl = string.IsNullOrWhiteSpace(request.ThumbnailUrl) ? null : request.ThumbnailUrl.Trim(),
            TargetPlaylistName = SongRequestRules.PlaylistName(now),
            Status = SongRequestStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
        };
        if (existing is null)
        {
            _context.SongRequests.Add(row);
        }

        if (song is not null)
        {
            // The instant path. One SaveChanges, so the Completed row and the links commit together or not at all.
            // Linked for audition, like a downloaded request: it lands in an audition playlist for the user to try.
            row.Status = SongRequestStatus.Completed;
            row.SongId = song.SongId;
            row.FailureReason = null;
            row.UpdatedAt = now;
            await ImportTargets.LinkAsync(_context, song, user.UserId, playlistId: null, row.TargetPlaylistName,
                importedForAudition: true, LikeStatus.Neutral, cancellationToken);
        }

        return await SaveAsync(row, cancellationToken);
    }

    private async Task<IActionResult> SaveAsync(SongRequest row, CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A concurrent request for the same song won the open-request index (or the same UserSong). Nothing of this
            // one was written; the app reads 409 as "already requested" and refreshes its list.
            return Conflict(new { message = "This song was already requested." });
        }

        return Ok(ToDto(row));
    }

    private static string? Validate(CreateSongRequestRequest? request)
    {
        if (request is null)
        {
            return "A request body is required.";
        }
        if (string.IsNullOrWhiteSpace(request.ExternalSongId))
        {
            return "ExternalSongId is required.";
        }
        if (request.ExternalSongId.Trim().Length > SongRequest.MaxExternalSongIdLength)
        {
            return $"ExternalSongId is longer than {SongRequest.MaxExternalSongIdLength} characters.";
        }
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return "Title is required.";
        }
        if (request.Title.Trim().Length > SongRequest.MaxTitleLength)
        {
            return $"Title is longer than {SongRequest.MaxTitleLength} characters.";
        }
        if (request.Artists is null)
        {
            return "Artists is required (it may be empty).";
        }
        if (request.Artists.Count > MaxArtists)
        {
            return $"At most {MaxArtists} artists.";
        }
        if (request.Artists.Any(a => string.IsNullOrWhiteSpace(a) || a.Trim().Length > MaxArtistNameLength))
        {
            return $"Each artist needs a name of at most {MaxArtistNameLength} characters.";
        }
        if (request.DurationSeconds is < 0)
        {
            return "DurationSeconds can't be negative.";
        }
        if (request.ThumbnailUrl is { } url && url.Trim().Length > SongRequest.MaxThumbnailUrlLength)
        {
            return $"ThumbnailUrl is longer than {SongRequest.MaxThumbnailUrlLength} characters.";
        }
        return null;
    }

    /// <summary>App: the caller's most recent requests, newest first, so it can show their status.</summary>
    [HttpGet]
    [Authorize]
    [QueryParameter("status", present: false)]
    public async Task<IActionResult> GetMine(CancellationToken cancellationToken)
    {
        if (HttpContext.Items["User"] is not User user)
        {
            return Unauthorized();
        }

        var rows = await _context.SongRequests
            .AsNoTracking()
            .Where(r => r.UserId == user.UserId)
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.SongRequestId)
            .Take(RecentLimit)
            .ToListAsync(cancellationToken);

        return Ok(rows.Select(ToDto).ToList());
    }

    /// <summary>
    /// App: cancel a request the producer hasn't claimed (Pending), or dismiss a Failed one. One conditional UPDATE,
    /// so a producer's Accepted that commits first wins and the cancel answers 409.
    /// </summary>
    [HttpDelete("{songRequestId}")]
    [Authorize]
    public async Task<IActionResult> Cancel(string songRequestId, CancellationToken cancellationToken)
    {
        if (HttpContext.Items["User"] is not User user)
        {
            return Unauthorized();
        }

        var now = UtcNow;
        var updated = await _context.SongRequests
            .Where(r => r.SongRequestId == songRequestId && r.UserId == user.UserId
                        && (r.Status == SongRequestStatus.Pending || r.Status == SongRequestStatus.Failed))
            .ExecuteUpdateAsync(set => set
                .SetProperty(r => r.Status, SongRequestStatus.Cancelled)
                .SetProperty(r => r.UpdatedAt, now), cancellationToken);
        if (updated > 0)
        {
            return NoContent();
        }

        var row = await _context.SongRequests.AsNoTracking()
            .FirstOrDefaultAsync(r => r.SongRequestId == songRequestId && r.UserId == user.UserId, cancellationToken);
        return row is null
            ? NotFound(new { message = "Song request not found." })
            : Conflict(ToDto(row));
    }

    /// <summary>Producer: the requests at <paramref name="status"/> (the funnel asks for Pending), oldest first.</summary>
    [HttpGet]
    [ProducerKey]
    [QueryParameter("status")]
    public async Task<IActionResult> GetQueue([FromQuery] SongRequestStatus status, CancellationToken cancellationToken)
    {
        var rows = await _context.SongRequests
            .AsNoTracking()
            .Where(r => r.Status == status)
            .OrderBy(r => r.CreatedAt)
            .ThenBy(r => r.SongRequestId)
            .Take(PollLimit)
            .ToListAsync(cancellationToken);

        return Ok(rows
            .Select(r => new PendingSongRequestDto(r.SongRequestId, r.UserId, r.ExternalSongId, r.Title, ArtistsOf(r),
                r.DurationSeconds, r.ThumbnailUrl, r.TargetPlaylistName))
            .ToList());
    }

    /// <summary>
    /// Producer: report progress on each request. Each ack is one conditional UPDATE that moves the request only from a
    /// state its status may follow (<see cref="SongRequestRules.AckMayFollow"/>), so a cancel or an import that commits
    /// first is never overwritten; that ack comes back in <see cref="SongRequestAckResponse.Rejected"/>. An ack of the
    /// status a request already has is applied again. <c>Unknown</c> or an undefined status is rejected.
    /// </summary>
    [HttpPost("ack")]
    [ProducerKey]
    public async Task<IActionResult> Ack([FromBody] SongRequestAckDto[] acks, CancellationToken cancellationToken)
    {
        if (acks is null || acks.Length == 0)
        {
            return Ok(new SongRequestAckResponse([], []));
        }
        if (acks.Length > MaxAckBatch)
        {
            return BadRequest(new { message = $"At most {MaxAckBatch} acks per call." });
        }

        var now = UtcNow;
        var applied = new List<string>();
        var rejected = new List<string>();
        foreach (var ack in acks.Where(a => a?.SongRequestId is not null).GroupBy(a => a.SongRequestId).Select(g => g.Last()))
        {
            var from = Enum.IsDefined(ack.Status) ? SongRequestRules.AckMayFollow(ack.Status) : [];
            if (from.Length == 0)
            {
                rejected.Add(ack.SongRequestId);
                continue;
            }

            var status = SongRequestRules.ToStatus(ack.Status);
            var reason = status is SongRequestStatus.Retrying or SongRequestStatus.Failed ? Truncate(ack.Reason) : null;
            var updated = await _context.SongRequests
                .Where(r => r.SongRequestId == ack.SongRequestId && from.Contains(r.Status))
                .ExecuteUpdateAsync(set => set
                    .SetProperty(r => r.Status, status)
                    .SetProperty(r => r.FailureReason, reason)
                    .SetProperty(r => r.UpdatedAt, now), cancellationToken);

            (updated > 0 ? applied : rejected).Add(ack.SongRequestId);
        }

        return Ok(new SongRequestAckResponse(applied, rejected));
    }

    private static string? Truncate(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? null
        : reason.Length <= SongRequest.MaxFailureReasonLength ? reason
        : reason[..SongRequest.MaxFailureReasonLength];

    private static List<string> ArtistsOf(SongRequest row) =>
        JsonSerializer.Deserialize<List<string>>(row.ArtistsJson) ?? [];

    private static SongRequestDto ToDto(SongRequest row) => new(
        row.SongRequestId, row.ExternalSongId, row.Title, ArtistsOf(row), row.DurationSeconds, row.ThumbnailUrl,
        row.Status, row.FailureReason, row.SongId, row.TargetPlaylistName, row.CreatedAt, row.UpdatedAt);
}
