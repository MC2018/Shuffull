using MediatR;
using Microsoft.AspNetCore.Mvc;
using Shuffull.Core.Features.Playlists.AddSongToPlaylist;
using Shuffull.Core.Features.Playlists.CreatePlaylist;
using Shuffull.Core.Features.Playlists.DeletePlaylist;
using Shuffull.Core.Features.Playlists.GetPlaylistDetails;
using Shuffull.Core.Features.Playlists.GetPlaylists;
using Shuffull.Core.Features.Playlists.GetUserPlaylists;
using Shuffull.Core.Features.Playlists.RemoveSongFromPlaylist;
using Shuffull.Core.Models.Database;
using Shuffull.Api.Extensions;
using Shuffull.Api.Tools.Authorization;

namespace Shuffull.Api.Controllers;

/// <summary>
/// CQRS-style playlists API. Thin controller: it resolves the authenticated user, dispatches the
/// request through MediatR, and maps the <see cref="Nut.Results.Result"/> to an HTTP response.
/// </summary>
[ApiController]
[Route("api/v1/playlists")]
public class PlaylistsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PlaylistsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetAll([FromCurrentUser] User user, CancellationToken cancellationToken)
    {
        return this.ToActionResult(await _mediator.Send(new GetUserPlaylistsQuery(user.UserId), cancellationToken));
    }

    [HttpPut]
    [Authorize]
    public async Task<IActionResult> Create([FromCurrentUser] User user, string name,
        CancellationToken cancellationToken)
    {
        return this.ToActionResult(await _mediator.Send(new CreatePlaylistCommand(user.UserId, name), cancellationToken));
    }

    [HttpPost("{playlistId}/songs")]
    [Authorize]
    public async Task<IActionResult> AddSong([FromCurrentUser] User user, string playlistId, [FromQuery] string songId,
        CancellationToken cancellationToken)
    {
        return this.ToActionResult(await _mediator.Send(new AddSongToPlaylistCommand(user.UserId, playlistId, songId), cancellationToken));
    }

    [HttpDelete("{playlistId}/songs/{songId}")]
    [Authorize]
    public async Task<IActionResult> RemoveSong([FromCurrentUser] User user, string playlistId, string songId,
        CancellationToken cancellationToken)
    {
        return this.ToActionResult(await _mediator.Send(new RemoveSongFromPlaylistCommand(user.UserId, playlistId, songId), cancellationToken));
    }

    // Deletes the whole playlist. For an audition (exploratory) playlist this also purges the songs the user
    // never kept — see DeletePlaylistHandler. Idempotent: deleting an already-absent playlist is a no-op success.
    [HttpDelete("{playlistId}")]
    [Authorize]
    public async Task<IActionResult> Delete([FromCurrentUser] User user, string playlistId,
        CancellationToken cancellationToken)
    {
        return this.ToActionResult(await _mediator.Send(new DeletePlaylistCommand(user.UserId, playlistId), cancellationToken));
    }

    [HttpPost("list")]
    [Authorize]
    public async Task<IActionResult> GetList([FromCurrentUser] User user, [FromBody] string[] playlistIds,
        CancellationToken cancellationToken)
    {
        return this.ToActionResult(await _mediator.Send(new GetPlaylistsQuery(user.UserId, playlistIds), cancellationToken));
    }

    [HttpGet("{playlistId}")]
    [Authorize]
    public async Task<IActionResult> Get([FromCurrentUser] User user, string playlistId,
        CancellationToken cancellationToken)
    {
        return this.ToActionResult(await _mediator.Send(new GetPlaylistDetailsQuery(user.UserId, playlistId), cancellationToken));
    }
}
