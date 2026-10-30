using MediatR;
using Microsoft.AspNetCore.Mvc;
using Shuffull.Api.Extensions;
using Shuffull.Api.Tools.Authorization;
using Shuffull.Core.Features.Listens.GetRecap;
using Shuffull.Core.Models.Database;

namespace Shuffull.Api.Controllers;

/// <summary>
/// The yearly recap (epic shuffull-workspace#35), derived from the caller's listen log. Thin controller: it resolves
/// the authenticated user, dispatches through MediatR, and maps the <see cref="Nut.Results.Result"/> to an HTTP response.
/// </summary>
[ApiController]
[Route("api/v1/recap")]
public class RecapController : ControllerBase
{
    private readonly IMediator _mediator;

    public RecapController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Any signed-in user: their recap of <paramref name="year"/>, from their own listens only. <paramref name="timeZone"/>
    /// is an IANA id (<c>Europe/Berlin</c>) or a fixed offset (<c>+02:00</c>); without one the recap is in UTC. An
    /// unknown zone or a year outside 2000-2999 is a 400. Computed on every request.
    /// </summary>
    [HttpGet("{year:int}")]
    [Authorize]
    public async Task<IActionResult> Get(int year, [FromQuery] string? timeZone, CancellationToken cancellationToken)
    {
        if (HttpContext.Items["User"] is not User user)
        {
            return Unauthorized();
        }

        return this.ToActionResult(await _mediator.Send(new GetRecapQuery(user.UserId, year, timeZone), cancellationToken));
    }
}
