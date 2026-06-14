using MediatR;
using Microsoft.AspNetCore.Mvc;
using Shuffull.Core.Features.Playlists.AddSongToPlaylist;
using Shuffull.Core.Features.Playlists.CreatePlaylist;
using Shuffull.Core.Models.Database;
using Shuffull.Site.Extensions;
using Shuffull.Site.Tools.Authorization;

namespace Shuffull.Site.Controllers;

/// <summary>
/// CQRS-style playlists API. Thin controller: it resolves the authenticated user, dispatches the
/// request through MediatR, and maps the <see cref="Nut.Results.Result"/> to an HTTP response. The
/// legacy <see cref="Shuffull.Tools.Controllers.PlaylistController"/> remains until the rest of the
/// playlist endpoints are migrated.
/// </summary>
[ApiController]
[Route("api/playlists")]
public class PlaylistsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PlaylistsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPut]
    [Authorize]
    public async Task<IActionResult> Create(string name, CancellationToken cancellationToken)
    {
        if (HttpContext.Items["User"] is not User user)
        {
            return Unauthorized();
        }

        return this.ToActionResult(await _mediator.Send(new CreatePlaylistCommand(user.UserId, name), cancellationToken));
    }

    [HttpPost("{playlistId}/songs")]
    [Authorize]
    public async Task<IActionResult> AddSong(string playlistId, [FromQuery] string songId, CancellationToken cancellationToken)
    {
        if (HttpContext.Items["User"] is not User user)
        {
            return Unauthorized();
        }

        return this.ToActionResult(await _mediator.Send(new AddSongToPlaylistCommand(user.UserId, playlistId, songId), cancellationToken));
    }
}
