using MediatR;
using Microsoft.AspNetCore.Mvc;
using Nut.Results;
using Shuffull.Api.Commands.Replacements.FlagSongReplacement;
using Shuffull.Api.Extensions;
using Shuffull.Api.Tools.Authorization;
using Shuffull.Core.Errors;
using Shuffull.Core.Features.Replacements.FlagSongReplacement;
using Shuffull.Core.Features.Replacements.GetSongReplacementQueue;
using Shuffull.Core.Models.Enums;

namespace Shuffull.Api.Controllers;

/// <summary>
/// Song-replacement queue. The app flags a poor-quality song here (authenticated); the producer pulls the
/// pending queue (shared-secret) to re-source a better version. Resolution happens automatically when a
/// replacement import lands (see SongImportService.ReplaceInDbAsync), which marks the request Completed.
/// </summary>
[ApiController]
[Route("api/v1/replacements")]
public class ReplacementsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ReplacementsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>App: flag a song for replacement. See <see cref="FlagSongReplacementHandler"/>.</summary>
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Flag([FromBody] FlagReplacementRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new FlagSongReplacementCommand(request.SongId, request.Note), cancellationToken);
        if (result.IsOk)
        {
            // An empty 200 whether or not a row was added, as the app has always had it.
            return Ok();
        }

        // A refusal answers with a plain-text message, not ToActionResult's { error } object, as it always has. Any
        // other kind (none today) keeps its own status, so a server fault is never sent to the app's outbox as a 4xx.
        var error = result.GetError();
        return ResultErrors.KindOf(error) switch
        {
            ErrorKind.NotFound => NotFound(error.Message),
            ErrorKind.Invalid => BadRequest(error.Message),
            _ => this.ToActionResult(result),
        };
    }

    /// <summary>Producer: pull the replacement queue at a given status (shared-secret guarded).</summary>
    [HttpGet]
    [ProducerKey]
    public async Task<IActionResult> GetQueue([FromQuery] SongReplacementStatus status, CancellationToken cancellationToken)
        => this.ToActionResult(await _mediator.Send(new GetSongReplacementQueueQuery(status), cancellationToken));
}
