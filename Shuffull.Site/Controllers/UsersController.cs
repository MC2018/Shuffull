using MediatR;
using Microsoft.AspNetCore.Mvc;
using Shuffull.Core.Features.Users.AuthenticateUser;
using Shuffull.Core.Features.Users.CreateUser;
using Shuffull.Core.Features.Users.GetCurrentUser;
using Shuffull.Core.Models.Database;
using Shuffull.Site.Extensions;
using Shuffull.Site.Tools.Authorization;

namespace Shuffull.Site.Controllers;

/// <summary>
/// CQRS-style users/account API. Thin controller: it dispatches each request through MediatR and
/// maps the <see cref="Nut.Results.Result"/> to an HTTP response. The legacy
/// <see cref="Shuffull.Tools.Controllers.UserController"/> remains until clients move onto these
/// routes.
/// </summary>
[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly IMediator _mediator;

    public UsersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("authenticate")]
    public async Task<IActionResult> Authenticate(string username, string userHash, CancellationToken cancellationToken)
    {
        return this.ToActionResult(await _mediator.Send(new AuthenticateUserCommand(username, userHash), cancellationToken));
    }

    [HttpPost]
    public async Task<IActionResult> Create(string username, string userHash, CancellationToken cancellationToken)
    {
        return this.ToActionResult(await _mediator.Send(new CreateUserCommand(username, userHash), cancellationToken));
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetCurrent(CancellationToken cancellationToken)
    {
        if (HttpContext.Items["User"] is not User user)
        {
            return Unauthorized();
        }

        return this.ToActionResult(await _mediator.Send(new GetCurrentUserQuery(user.UserId), cancellationToken));
    }
}
