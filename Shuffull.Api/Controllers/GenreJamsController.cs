using MediatR;
using Microsoft.AspNetCore.Mvc;
using Shuffull.Api.Commands.GenreJams.SaveGenreJam;
using Shuffull.Api.Extensions;
using Shuffull.Api.Tools.Authorization;
using Shuffull.Core.Features.GenreJams.DeleteGenreJam;
using Shuffull.Core.Features.GenreJams.GetGenreJams;
using Shuffull.Core.Features.GenreJams.SaveGenreJam;
using Shuffull.Core.Models.Database;

namespace Shuffull.Api.Controllers;

/// <summary>
/// The caller's saved Genre Jams, so a jam saved on one device shows up on their others. The app writes through its
/// outbox (<c>PUT</c> and <c>DELETE</c>, both safe to resend) and reads the whole list on each sync. Thin controller:
/// it resolves the authenticated user, dispatches through MediatR, and maps the <see cref="Nut.Results.Result"/>.
/// </summary>
[ApiController]
[Route("api/v1/genre-jams")]
public class GenreJamsController : ControllerBase
{
    private readonly IMediator _mediator;

    public GenreJamsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Any signed-in user: every Genre Jam they have saved, by name.</summary>
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        if (HttpContext.Items["User"] is not User user)
        {
            return Unauthorized();
        }

        return this.ToActionResult(await _mediator.Send(new GetGenreJamsQuery(user.UserId), cancellationToken));
    }

    /// <summary>
    /// Any signed-in user: create their jam <paramref name="genreJamId"/> (an id the app generated), or replace it whole.
    /// 409 if the id belongs to another user.
    /// </summary>
    [HttpPut("{genreJamId}")]
    [Authorize]
    public async Task<IActionResult> Save(string genreJamId, [FromBody] SaveGenreJamRequest request, CancellationToken cancellationToken)
    {
        if (HttpContext.Items["User"] is not User user)
        {
            return Unauthorized();
        }

        var command = new SaveGenreJamCommand(
            user.UserId, genreJamId, request.Name ?? "", request.Whitelists, request.Blacklists, request.HeavinessMin, request.HeavinessMax);
        return this.ToActionResult(await _mediator.Send(command, cancellationToken));
    }

    /// <summary>Any signed-in user: delete their jam <paramref name="genreJamId"/>. Succeeds if it is already gone.</summary>
    [HttpDelete("{genreJamId}")]
    [Authorize]
    public async Task<IActionResult> Delete(string genreJamId, CancellationToken cancellationToken)
    {
        if (HttpContext.Items["User"] is not User user)
        {
            return Unauthorized();
        }

        return this.ToActionResult(await _mediator.Send(new DeleteGenreJamCommand(user.UserId, genreJamId), cancellationToken));
    }
}
