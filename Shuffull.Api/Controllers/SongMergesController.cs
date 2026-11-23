using MediatR;
using Microsoft.AspNetCore.Mvc;
using Shuffull.Api.Extensions;
using Shuffull.Api.Tools.Authorization;
using Shuffull.Core.Features.SongMerges.GetSongMerges;

namespace Shuffull.Api.Controllers;

/// <summary>
/// The merge feed (Shuffull#142). A curator merges two copies of a song through <c>POST /api/v1/songs/merge</c>; every
/// device pages this feed to swap the merged-away id for the survivor's in its local copy and its outbox. Thin
/// controller: it dispatches through MediatR and maps the <see cref="Nut.Results.Result"/>.
/// </summary>
[ApiController]
[Route("api/v1/song-merges")]
public class SongMergesController : ControllerBase
{
    private readonly IMediator _mediator;

    public SongMergesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Any signed-in user: merges after (<paramref name="afterDate"/>, <paramref name="afterId"/>), 500 per page.
    /// </summary>
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Get([FromQuery] DateTime afterDate, [FromQuery] string? afterId, CancellationToken cancellationToken)
        => this.ToActionResult(await _mediator.Send(new GetSongMergesQuery(afterDate, afterId), cancellationToken));
}
