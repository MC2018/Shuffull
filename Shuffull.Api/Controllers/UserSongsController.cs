using MediatR;
using Microsoft.AspNetCore.Mvc;
using Shuffull.Core.Features.UserSongs.CreateUserSongs;
using Shuffull.Core.Features.UserSongs.GetUserSongs;
using Shuffull.Core.Features.UserSongs.SetSongLikeStatus;
using Shuffull.Core.Features.UserSongs.UpdateSongsLastPlayed;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;
using Shuffull.Api.Extensions;
using Shuffull.Api.Tools.Authorization;

namespace Shuffull.Api.Controllers;

/// <summary>
/// CQRS-style user-song (play record) API. Thin controller: it resolves the authenticated user,
/// dispatches the request through MediatR, and maps the <see cref="Nut.Results.Result"/> to an HTTP
/// response.
/// </summary>
[ApiController]
[Route("api/v1/user-songs")]
public class UserSongsController : ControllerBase
{
    private readonly IMediator _mediator;

    public UserSongsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPut]
    [Authorize]
    public async Task<IActionResult> CreateMany([FromCurrentUser] User user, [FromBody] string[] songIds,
        CancellationToken cancellationToken)
    {
        return this.ToActionResult(await _mediator.Send(new CreateUserSongsCommand(user.UserId, songIds), cancellationToken));
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetAll([FromCurrentUser] User user, [FromQuery] DateTime afterDate,
        [FromQuery] string? afterId, CancellationToken cancellationToken)
    {
        return this.ToActionResult(await _mediator.Send(new GetUserSongsQuery(user.UserId, afterDate, afterId), cancellationToken));
    }

    [HttpPost("last-played")]
    [Authorize]
    public async Task<IActionResult> UpdateLastPlayed([FromCurrentUser] User user,
        [FromBody] List<SongLastPlayed> updates, CancellationToken cancellationToken)
    {
        return this.ToActionResult(await _mediator.Send(new UpdateSongsLastPlayedCommand(user.UserId, updates), cancellationToken));
    }

    [HttpPost("{songId}/like")]
    [Authorize]
    public async Task<IActionResult> SetLikeStatus([FromCurrentUser] User user, string songId,
        [FromBody] LikeStatus likeStatus, CancellationToken cancellationToken)
    {
        return this.ToActionResult(await _mediator.Send(new SetSongLikeStatusCommand(user.UserId, songId, likeStatus), cancellationToken));
    }
}
