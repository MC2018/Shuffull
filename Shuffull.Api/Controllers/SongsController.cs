using MediatR;
using Microsoft.AspNetCore.Mvc;
using Shuffull.Api.Extensions;
using Shuffull.Core.Features.Songs.GetSong;
using Shuffull.Core.Features.Songs.GetSongList;
using Shuffull.Core.Features.Songs.GetSongPage;
using Shuffull.Core.Features.Songs.GetSongsChanged;
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
}
