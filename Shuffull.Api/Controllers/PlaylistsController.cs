using MediatR;
using Microsoft.AspNetCore.Mvc;
using Shuffull.Core.Features.Playlists.AddSongToPlaylist;
using Shuffull.Core.Features.Playlists.CreatePlaylist;
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
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        if (HttpContext.Items["User"] is not User user)
        {
            return Unauthorized();
        }

        return this.ToActionResult(await _mediator.Send(new GetUserPlaylistsQuery(user.UserId), cancellationToken));
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

    [HttpDelete("{playlistId}/songs/{songId}")]
    [Authorize]
    public async Task<IActionResult> RemoveSong(string playlistId, string songId, CancellationToken cancellationToken)
    {
        if (HttpContext.Items["User"] is not User user)
        {
            return Unauthorized();
        }

        return this.ToActionResult(await _mediator.Send(new RemoveSongFromPlaylistCommand(user.UserId, playlistId, songId), cancellationToken));
    }

    [HttpPost("list")]
    [Authorize]
    public async Task<IActionResult> GetList([FromBody] string[] playlistIds, CancellationToken cancellationToken)
    {
        if (HttpContext.Items["User"] is not User user)
        {
            return Unauthorized();
        }

        return this.ToActionResult(await _mediator.Send(new GetPlaylistsQuery(user.UserId, playlistIds), cancellationToken));
    }

    [HttpGet("{playlistId}")]
    [Authorize]
    public async Task<IActionResult> Get(string playlistId, CancellationToken cancellationToken)
    {
        if (HttpContext.Items["User"] is not User user)
        {
            return Unauthorized();
        }

        return this.ToActionResult(await _mediator.Send(new GetPlaylistDetailsQuery(user.UserId, playlistId), cancellationToken));
    }
}
