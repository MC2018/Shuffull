using MediatR;
using Microsoft.AspNetCore.Mvc;
using Shuffull.Api.Commands.Devices.CheckInDevice;
using Shuffull.Api.Extensions;
using Shuffull.Api.Tools.Authorization;
using Shuffull.Core.Features.Devices.CheckInDevice;
using Shuffull.Core.Models.Database;

namespace Shuffull.Api.Controllers;

/// <summary>
/// The caller's app installs (Shuffull#132). Thin controller: it resolves the authenticated user, dispatches through
/// MediatR, and maps the <see cref="Nut.Results.Result"/> to an HTTP response.
/// </summary>
[ApiController]
[Route("api/v1/devices")]
public class DevicesController : ControllerBase
{
    private readonly IMediator _mediator;

    public DevicesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Any signed-in user: check in one of their own installs. Creates the row the first time, then updates its name
    /// and platform and sets <c>LastSeenAt</c> to now. Returns the row. An empty or over-long id or name is a 400.
    /// </summary>
    [HttpPut("{deviceId}")]
    [Authorize]
    public async Task<IActionResult> CheckIn([FromCurrentUser] User user, string deviceId, [FromBody] CheckInDeviceRequest request,
        CancellationToken cancellationToken)
    {
        return this.ToActionResult(await _mediator.Send(
            new CheckInDeviceCommand(user.UserId, deviceId, request.Name, request.Platform), cancellationToken));
    }
}
