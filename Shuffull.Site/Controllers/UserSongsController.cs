using MediatR;
using Microsoft.AspNetCore.Mvc;
using Shuffull.Core.Features.UserSongs.CreateUserSongs;
using Shuffull.Core.Features.UserSongs.GetUserSongs;
using Shuffull.Core.Features.UserSongs.UpdateSongsLastPlayed;
using Shuffull.Core.Models.Database;
using Shuffull.Site.Extensions;
using Shuffull.Site.Tools.Authorization;

namespace Shuffull.Site.Controllers;

/// <summary>
/// CQRS-style user-song (play record) API. Thin controller: it resolves the authenticated user,
/// dispatches the request through MediatR, and maps the <see cref="Nut.Results.Result"/> to an HTTP
/// response. The legacy <see cref="Shuffull.Tools.Controllers.UserSongController"/> remains until
/// clients move onto these routes.
/// </summary>
[ApiController]
[Route("api/user-songs")]
public class UserSongsController : ControllerBase
{
    private readonly IMediator _mediator;

    public UserSongsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPut]
    [Authorize]
    public async Task<IActionResult> CreateMany([FromBody] string[] songIds, CancellationToken cancellationToken)
    {
        if (HttpContext.Items["User"] is not User user)
        {
            return Unauthorized();
        }

        return this.ToActionResult(await _mediator.Send(new CreateUserSongsCommand(user.UserId, songIds), cancellationToken));
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetAll([FromQuery] DateTime afterDate, CancellationToken cancellationToken)
    {
        if (HttpContext.Items["User"] is not User user)
        {
            return Unauthorized();
        }

        return this.ToActionResult(await _mediator.Send(new GetUserSongsQuery(user.UserId, afterDate), cancellationToken));
    }

    [HttpPost("last-played")]
    [Authorize]
    public async Task<IActionResult> UpdateLastPlayed([FromBody] List<SongLastPlayed> updates, CancellationToken cancellationToken)
    {
        if (HttpContext.Items["User"] is not User user)
        {
            return Unauthorized();
        }

        return this.ToActionResult(await _mediator.Send(new UpdateSongsLastPlayedCommand(user.UserId, updates), cancellationToken));
    }
}
