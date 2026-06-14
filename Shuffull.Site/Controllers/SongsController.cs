using MediatR;
using Microsoft.AspNetCore.Mvc;
using Shuffull.Site.Extensions;
using Shuffull.Core.Features.Songs.GetSong;
using Shuffull.Site.Tools.Authorization;

namespace Shuffull.Site.Controllers;

/// <summary>
/// CQRS-style songs API. Thin controller: it only dispatches the request through MediatR and maps
/// the <see cref="Nut.Results.Result"/> to an HTTP response. This is the reference template for the
/// Sociallite-mirroring restructure; the legacy <see cref="SongController"/> remains until the rest
/// of the endpoints are migrated.
/// </summary>
[ApiController]
[Route("api/songs")]
public class SongsController : ControllerBase
{
    private readonly IMediator _mediator;

    public SongsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("{songId}")]
    [Authorize]
    public async Task<IActionResult> GetSong(string songId, CancellationToken cancellationToken)
        => this.ToActionResult(await _mediator.Send(new GetSongQuery(songId), cancellationToken));
}
