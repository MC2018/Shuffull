using MediatR;
using Microsoft.AspNetCore.Mvc;
using Shuffull.Api.Commands.YoutubeRatings.AckYoutubeRatings;
using Shuffull.Api.Extensions;
using Shuffull.Api.Tools.Authorization;
using Shuffull.Core.Alerts;
using Shuffull.Core.Features.YoutubeRatings.AckYoutubeRatings;
using Shuffull.Core.Features.YoutubeRatings.GetYoutubeRatingQueue;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Services.Alerts;

namespace Shuffull.Api.Controllers;

/// <summary>
/// Like-parity queue. When a user's sentiment toward a YouTube-sourced song changes, the site records a pending
/// <see cref="Core.Models.Database.YoutubeRatingRequest"/> (see SetSongLikeStatusHandler). The producer (funnel)
/// pulls the pending rows with the shared secret, applies each via the YouTube Data API <c>videos.rate</c>, then
/// acks them. Producer-only (shared secret) — the app never calls this; it just sets like status.
/// </summary>
[ApiController]
[ProducerKey]
[Route("api/v1/youtube-ratings")]
public class YoutubeRatingsController : ControllerBase
{
    private readonly IMediator _mediator;

    public YoutubeRatingsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Producer: pull the rating queue at a given status (defaults to Pending). Shared-secret guarded.</summary>
    [HttpGet]
    public async Task<IActionResult> GetQueue([FromServices] IHealthSignals signals,
        [FromQuery] YoutubeRatingStatus status, CancellationToken cancellationToken)
    {
        // The funnel's like-sync asks for Pending every 10 minutes; an hour without it alerts (Shuffull#75).
        if (status == YoutubeRatingStatus.Pending)
        {
            await signals.SuccessAsync(ShuffullSignalRules.YoutubeRatingsPoll, cancellationToken);
        }

        return this.ToActionResult(await _mediator.Send(new GetYoutubeRatingQueueQuery(status), cancellationToken));
    }

    /// <summary>Producer: report what happened to each rating. See <see cref="AckYoutubeRatingsHandler"/>.</summary>
    [HttpPost("ack")]
    public async Task<IActionResult> Ack([FromBody] YoutubeRatingAckItem[] items, CancellationToken cancellationToken)
    {
        // An empty ack has always been a bare 200, with no counts.
        if (items is null || items.Length == 0)
        {
            return Ok();
        }

        var acks = items.Select(i => new YoutubeRatingAck(i.RatingRequestId, i.Rating, i.Outcome)).ToList();
        return this.ToActionResult(await _mediator.Send(new AckYoutubeRatingsCommand(acks), cancellationToken));
    }
}
