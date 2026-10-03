using MediatR;
using Microsoft.AspNetCore.Mvc;
using Shuffull.Api.Commands.Songs.RetagSongs;
using Shuffull.Api.Commands.Songs.UpdateSong;
using Shuffull.Api.Extensions;
using Shuffull.Core.Features.Songs.GetSong;
using Shuffull.Core.Features.Songs.GetSongList;
using Shuffull.Core.Features.Songs.GetSongPage;
using Shuffull.Core.Features.Songs.GetSongsChanged;
using Shuffull.Core.Features.Songs.RetagSongs;
using Shuffull.Core.Features.Songs.ApplySongTags;
using Shuffull.Core.Features.Songs.GetPendingTagSongs;
using Shuffull.Core.Features.Songs.RetagStaleSongs;
using Shuffull.Core.Features.Songs.UpdateSong;
using Shuffull.Api.Tools.Authorization;

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
    private readonly IConfiguration _configuration;

    public SongsController(IMediator mediator, IConfiguration configuration)
    {
        _mediator = mediator;
        _configuration = configuration;
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetSongPage([FromQuery] int pageIndex, CancellationToken cancellationToken)
        => this.ToActionResult(await _mediator.Send(new GetSongPageQuery(pageIndex), cancellationToken));

    /// <summary>
    /// Incremental song sync: songs whose <c>Version</c> is newer than the client's cursor, paged and ordered
    /// by version. Lets the app refresh songs it already holds (e.g. an in-place replacement) — the full-page
    /// <see cref="GetSongPage"/> / list endpoints only seed songs the client doesn't have yet.
    /// </summary>
    [HttpGet("changed")]
    [Authorize]
    public async Task<IActionResult> GetSongsChanged([FromQuery] DateTime afterDate, CancellationToken cancellationToken)
        => this.ToActionResult(await _mediator.Send(new GetSongsChangedQuery(afterDate), cancellationToken));

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
            new UpdateSongCommand(songId, request.Name, request.Bpm, request.Energy, request.Artists, request.Tags),
            cancellationToken));

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
    /// should be tagged at. Shared-secret guarded — this is the funnel's work queue, not a user endpoint.
    /// </summary>
    [HttpGet("pending-tags")]
    public async Task<IActionResult> GetPendingTagSongs(
        [FromQuery] int limit,
        [FromHeader(Name = "X-Import-Key")] string? importKey,
        CancellationToken cancellationToken)
    {
        if (!IsProducer(importKey))
        {
            return Unauthorized();
        }

        return this.ToActionResult(await _mediator.Send(new GetPendingTagSongsQuery(limit), cancellationToken));
    }

    /// <summary>
    /// Producer: write back the tags an external engine generated, stamped with the model that produced them.
    /// Shared-secret guarded. Does not touch name/artists and never sets MetadataLocked — see
    /// <see cref="ApplySongTagsCommand"/> for why this is not UpdateSong.
    /// </summary>
    [HttpPost("{songId}/tags")]
    public async Task<IActionResult> ApplySongTags(
        string songId,
        [FromBody] ApplySongTagsRequest request,
        [FromHeader(Name = "X-Import-Key")] string? importKey,
        CancellationToken cancellationToken)
    {
        if (!IsProducer(importKey))
        {
            return Unauthorized();
        }

        return this.ToActionResult(
            await _mediator.Send(new ApplySongTagsCommand(songId, request.Tags, request.TagModel), cancellationToken));
    }

    /// <summary>Shared secret shared with the producer, same key the import/rating queues use.</summary>
    private bool IsProducer(string? importKey)
    {
        var expected = _configuration["Shuffull:Import:Key"];
        return !string.IsNullOrEmpty(expected) && importKey == expected;
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
