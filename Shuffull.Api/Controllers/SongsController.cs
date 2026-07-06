using MediatR;
using Microsoft.AspNetCore.Mvc;
using Shuffull.Api.Extensions;
using Shuffull.Core.Features.Songs.GetSong;
using Shuffull.Core.Features.Songs.GetSongList;
using Shuffull.Core.Features.Songs.GetSongPage;
using Shuffull.Core.Features.Songs.GetSongsChanged;
using Shuffull.Core.Features.Songs.RetagSong;
using Shuffull.Core.Features.Songs.RetagStaleSongs;
using Shuffull.Core.Features.Songs.UpdateSong;
using Shuffull.Api.Tools.Authorization;

namespace Shuffull.Api.Controllers;

/// <summary>Body for the curator song-metadata edit (the song id comes from the route).</summary>
public record UpdateSongRequest(
    string Name,
    int? Bpm,
    int? Energy,
    IReadOnlyList<string> Artists,
    IReadOnlyList<SongTagEdit> Tags);

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

    public SongsController(IMediator mediator)
    {
        _mediator = mediator;
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
    /// Curator-only: re-tag one song from its stored inputs with the current strong model (no re-download).
    /// Curator-locked songs are left untouched. Bumps the song's version so clients re-sync it.
    /// </summary>
    [HttpPost("{songId}/retag")]
    [Authorize]
    public async Task<IActionResult> RetagSong(string songId, CancellationToken cancellationToken)
        => this.ToActionResult(await _mediator.Send(new RetagSongCommand(songId), cancellationToken));

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
