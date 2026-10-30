using MediatR;
using Microsoft.AspNetCore.Mvc;
using Shuffull.Api.Commands.Listens.RecordListens;
using Shuffull.Api.Extensions;
using Shuffull.Api.Tools.Authorization;
using Shuffull.Core.Features.Listens.RecordListens;
using Shuffull.Core.Models.Database;

namespace Shuffull.Api.Controllers;

/// <summary>
/// The listen log: one row per play, written by the app's outbox. Thin controller: it resolves the authenticated
/// user, dispatches through MediatR, and maps the <see cref="Nut.Results.Result"/> to an HTTP response.
/// </summary>
[ApiController]
[Route("api/v1/listens")]
public class ListensController : ControllerBase
{
    private readonly IMediator _mediator;

    public ListensController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Any signed-in user: record a batch of their own listens, at most <see cref="RecordListensHandler.MaxBatch"/>.
    /// Returns one outcome per listen, in order: "recorded", "duplicate" (already recorded; a resend is safe) or
    /// "rejected" with an error (invalid, or not the caller's).
    /// </summary>
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Record([FromBody] RecordListensRequest request, CancellationToken cancellationToken)
    {
        if (HttpContext.Items["User"] is not User user)
        {
            return Unauthorized();
        }

        return this.ToActionResult(await _mediator.Send(new RecordListensCommand(user.UserId, request.Listens ?? []), cancellationToken));
    }
}
