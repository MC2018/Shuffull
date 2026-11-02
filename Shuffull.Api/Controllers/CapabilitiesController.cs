using MediatR;
using Microsoft.AspNetCore.Mvc;
using Shuffull.Api.Extensions;
using Shuffull.Api.Tools.Authorization;
using Shuffull.Core.Features.Capabilities.GetCapabilities;

namespace Shuffull.Api.Controllers;

/// <summary>
/// Which optional features this server has set up (<see cref="CapabilitiesResponse"/>), so the app shows only those.
/// Read from configuration, so it changes only with a redeploy.
/// </summary>
[ApiController]
[Route("api/v1/capabilities")]
public class CapabilitiesController : ControllerBase
{
    private readonly IMediator _mediator;

    public CapabilitiesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
        => this.ToActionResult(await _mediator.Send(new GetCapabilitiesQuery(), cancellationToken));
}
