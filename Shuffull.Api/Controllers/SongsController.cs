using MediatR;
using Microsoft.AspNetCore.Mvc;
using Shuffull.Api.Commands.Songs.KeepSongs;
using Shuffull.Api.Commands.Songs.RetagSongs;
using Shuffull.Api.Commands.Songs.UpdateSong;
using Shuffull.Api.Extensions;
using Shuffull.Core.Features.Songs.GetSong;
using Shuffull.Core.Features.Songs.GetSongList;
using Shuffull.Core.Features.Songs.GetSongPage;
using Shuffull.Core.Features.Songs.GetSongsChanged;
using Shuffull.Core.Features.Songs.KeepSongs;
using Shuffull.Core.Features.Songs.RetagSongs;
using Shuffull.Core.Features.Songs.ApplySongTags;
using Shuffull.Core.Features.Songs.GetPendingTagSongs;
using Shuffull.Core.Features.Songs.GetLibrarySongs;
using Shuffull.Core.Features.Songs.RetagStaleSongs;
using Shuffull.Core.Features.Songs.UpdateSong;
using Shuffull.Api.Tools.Authorization;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Alerts;
using Shuffull.Core.Services.Alerts;

namespace Shuffull.Api.Controllers;

/// <summary>
/// CQRS-style songs API. Thin controller: it only dispatches the request through MediatR and maps
/// the <see cref="Nut.Results.Result"/> to an HTTP response. This is the reference template for the
/// Sociallite-mirroring restructure.
/// </summary>
[ApiController]
[Route("api/v1/songs")]
public class SongsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IHealthSignals _signals;

    public SongsController(IMediator mediator, IHealthSignals signals)
    {
        _mediator = mediator;
        _signals = signals;
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetSongPage([FromQuery] int pageIndex, CancellationToken cancellationToken)
        => this.ToActionResult(await _mediator.Send(new GetSongPageQuery(pageIndex), cancellationToken));

    /// <summary>
    /// Incremental song sync: songs whose <c>Version</c> is newer than the client's cursor, paged and ordered
    /// by version. Lets the app refresh songs it already holds (e.g. an in-place replacement) — the full-page
    /// <see cref="GetSongPage"/> / list endpoints only seed songs the client doesn't have yet. Send the last song's
    /// exact <c>version</c> string and <c>songId</c> as <c>afterDate</c> + <c>afterId</c> to page by (Version, SongId),
    /// which steps through any number of songs sharing one Version; <c>afterDate</c> alone is the old cursor.
    /// </summary>
    [HttpGet("changed")]
    [Authorize]
    public async Task<IActionResult> GetSongsChanged([FromQuery] DateTime afterDate, [FromQuery] string? afterId, CancellationToken cancellationToken)
        => this.ToActionResult(await _mediator.Send(new GetSongsChangedQuery(afterDate, afterId), cancellationToken));

    [HttpGet("{songId}")]
    [Authorize]
    public async Task<IActionResult> GetSong(string songId, CancellationToken cancellationToken)
        => this.ToActionResult(await _mediator.Send(new GetSongQuery(songId), cancellationToken));

    [HttpPost("list")]
    [Authorize]
    public async Task<IActionResult> GetSongList([FromBody] string[] songIds, CancellationToken cancellationToken)
        => this.ToActionResult(await _mediator.Send(new GetSongListQuery(songIds), cancellationToken));

    /// <summary>
    /// Curator-only metadata edit (fix mislabels). [Authorize] ensures the caller is signed in; the
    /// <c>[RequiresRole(Role.Curator)]</c> on the command then rejects non-curators in the pipeline. Bumps the
    /// song's version so every client re-syncs the corrected record.
    /// </summary>
    [HttpPut("{songId}")]
    [Authorize]
    public async Task<IActionResult> UpdateSong(string songId, [FromBody] UpdateSongRequest request, CancellationToken cancellationToken)
        => this.ToActionResult(await _mediator.Send(
            new UpdateSongCommand(songId, request.Name, request.Bpm, request.Heaviness, request.Artists, request.Tags),
            cancellationToken));

    /// <summary>
    /// Any signed-in user: record a Keep of audition songs in their library — clears <c>Exploratory</c>, runs no
    /// AI. Returns a per-song outcome ("kept" | "failed", same shape as <see cref="RetagSongs"/>). Separate from
    /// re-tag on purpose: the Keep is the user's decision, re-tagging is curator work, and sharing a gate meant a
    /// non-curator's Keep was never recorded.
    /// </summary>
    [HttpPost("keep")]
    [Authorize]
    public async Task<IActionResult> KeepSongs([FromBody] KeepSongsRequest request, CancellationToken cancellationToken)
    {
        if (HttpContext.Items["User"] is not User user)
        {
            return Unauthorized();
        }

        return this.ToActionResult(await _mediator.Send(new KeepSongsCommand(user.UserId, request.SongIds ?? []), cancellationToken));
    }

    /// <summary>
    /// Curator-only: force re-tag songs from their stored inputs (no re-download). Per-item model tiers
    /// ("weak" = the budget engine for an audition Keep; null/"strong" = full quality for likes/upgrades), so
    /// one call flushes a mixed offline backlog. Duplicate ids collapse stronger-wins. Curator-locked songs
    /// are reported skipped. Returns a per-song outcome; bumps the version of each song actually re-tagged.
    /// </summary>
    [HttpPost("retag")]
    [Authorize]
    public async Task<IActionResult> RetagSongs([FromBody] RetagSongsRequest request, CancellationToken cancellationToken)
        => this.ToActionResult(await _mediator.Send(new RetagSongsCommand(request.Items ?? []), cancellationToken));

    /// <summary>
    /// Producer: the songs whose tags are behind the model they deserve, oldest first, with the tier each
    /// should be tagged at. Shared-secret guarded — this is the funnel's work queue, not a user endpoint. Not
    /// read-only: every song returned counts an attempt and is held back until its retry delay passes.
    /// </summary>
    [HttpGet("pending-tags")]
    [ProducerKey]
    public async Task<IActionResult> GetPendingTagSongs(
        [FromQuery] int limit,
        CancellationToken cancellationToken)
    {
        // Proves the funnel is still asking for work; an hour without it alerts (Shuffull#75). Throttled to a write a minute.
        await _signals.SuccessAsync(ShuffullSignalRules.PendingTagsPoll, cancellationToken);
        return this.ToActionResult(await _mediator.Send(new GetPendingTagSongsQuery(limit), cancellationToken));
    }

    /// <summary>
    /// Producer: every library song, keyset-paged by id, so the funnel's duplicate registry also covers songs it
    /// never exported (the pre-funnel library, manual imports). Shared-secret guarded.
    /// </summary>
    [HttpGet("library")]
    [ProducerKey]
    public async Task<IActionResult> GetLibrarySongs(
        [FromQuery] string? after,
        [FromQuery] int limit,
        CancellationToken cancellationToken)
        => this.ToActionResult(await _mediator.Send(new GetLibrarySongsQuery(after, limit), cancellationToken));

    /// <summary>
    /// Producer: write back the tags an external engine generated, stamped with the model that produced them.
    /// Shared-secret guarded. Does not touch name/artists and never sets MetadataLocked — see
    /// <see cref="ApplySongTagsCommand"/> for why this is not UpdateSong.
    /// </summary>
    [HttpPost("{songId}/tags")]
    [ProducerKey]
    public async Task<IActionResult> ApplySongTags(
        string songId,
        [FromBody] ApplySongTagsRequest request,
        CancellationToken cancellationToken)
    {
        return this.ToActionResult(
            await _mediator.Send(new ApplySongTagsCommand(songId, request.Tags, request.TagModel, request.TagRevision), cancellationToken));
    }

    /// <summary>
    /// Curator-only: re-tag a bounded batch of stale songs (TagModel weaker than the current strong model).
    /// Returns how many were enriched and how many remain, so the caller drives a whole-library upgrade in
    /// chunks. NOTE: on first run the entire pre-provenance library is stale (null TagModel).
    /// </summary>
    [HttpPost("retag-stale")]
    [Authorize]
    public async Task<IActionResult> RetagStaleSongs([FromQuery] int limit, CancellationToken cancellationToken)
        => this.ToActionResult(await _mediator.Send(new RetagStaleSongsCommand(limit), cancellationToken));
}
