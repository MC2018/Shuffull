using MediatR;
using Microsoft.AspNetCore.Mvc;
using Shuffull.Core.Features.Tags.AddTagToSong;
using Shuffull.Core.Features.Tags.GetAllTags;
using Shuffull.Api.Extensions;
using Shuffull.Api.Tools.Authorization;

namespace Shuffull.Api.Controllers;

/// <summary>
/// CQRS-style tags API. Thin controller: it dispatches each request through MediatR and maps the
/// <see cref="Nut.Results.Result"/> to an HTTP response.
/// </summary>
[ApiController]
[Route("api/v1/tags")]
public class TagsController : ControllerBase
{
    private readonly IMediator _mediator;

    public TagsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        return this.ToActionResult(await _mediator.Send(new GetAllTagsQuery(), cancellationToken));
    }

    [HttpPost("{tagId}/songs")]
    [Authorize]
    public async Task<IActionResult> AddToSong(string tagId, [FromQuery] string songId, CancellationToken cancellationToken)
    {
        return this.ToActionResult(await _mediator.Send(new AddTagToSongCommand(songId, tagId), cancellationToken));
    }
}
