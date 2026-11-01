using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Shuffull.Core.Features.Users.AuthenticateUser;
using Shuffull.Core.Features.Users.ChangePassword;
using Shuffull.Core.Features.Users.CreateUser;
using Shuffull.Core.Features.Users.GetCurrentUser;
using Shuffull.Core.Models.Database;
using Shuffull.Api.Commands.Users.Credentials;
using Shuffull.Api.Extensions;
using Shuffull.Api.Tools.Authorization;

namespace Shuffull.Api.Controllers;

/// <summary>
/// CQRS-style users/account API. Thin controller: it dispatches each request through MediatR and
/// maps the <see cref="Nut.Results.Result"/> to an HTTP response.
/// </summary>
[ApiController]
[Route("api/v1/users")]
public class UsersController : ControllerBase
{
    private readonly IMediator _mediator;

    public UsersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    // Credentials travel in the JSON body. The query string is the old form: anything in a URL lands in proxy and
    // access logs, and the client hash is all it takes to log in. It stays readable only so apps installed before
    // the change can still sign in; removing it is shuffull-workspace#58.
    [HttpPost("authenticate")]
    public async Task<IActionResult> Authenticate(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CredentialsRequest? body,
        [FromQuery] string? username,
        [FromQuery] string? userHash,
        CancellationToken cancellationToken)
    {
        var credentials = body ?? new CredentialsRequest(username, userHash);
        return this.ToActionResult(await _mediator.Send(
            new AuthenticateUserCommand(credentials.Username ?? "", credentials.UserHash ?? ""), cancellationToken));
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CredentialsRequest? body,
        [FromQuery] string? username,
        [FromQuery] string? userHash,
        CancellationToken cancellationToken)
    {
        var credentials = body ?? new CredentialsRequest(username, userHash);
        return this.ToActionResult(await _mediator.Send(
            new CreateUserCommand(credentials.Username ?? "", credentials.UserHash ?? ""), cancellationToken));
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

    // Signs out every other token the user holds (JwtMiddleware checks the security stamp) and returns a fresh session
    // for this device. A wrong current password is a 400, so the app doesn't log out over a typo.
    [HttpPost("me/password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest body, CancellationToken cancellationToken)
    {
        if (HttpContext.Items["User"] is not User user)
        {
            return Unauthorized();
        }

        return this.ToActionResult(await _mediator.Send(
            new ChangePasswordCommand(user.UserId, body.CurrentUserHash ?? "", body.NewUserHash ?? ""), cancellationToken));
    }
}
