using MediatR;
using Microsoft.AspNetCore.Mvc;
using Shuffull.Api.Commands.SongRequests;
using Shuffull.Api.Extensions;
using Shuffull.Api.Tools;
using Shuffull.Api.Tools.Authorization;
using Shuffull.Core.Features.SongRequests;
using Shuffull.Core.Features.SongRequests.AckSongRequests;
using Shuffull.Core.Features.SongRequests.CancelSongRequest;
using Shuffull.Core.Features.SongRequests.CreateSongRequest;
using Shuffull.Core.Features.SongRequests.GetMySongRequests;
using Shuffull.Core.Features.SongRequests.GetSongRequestQueue;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;
using Shuffull.Metadata.Contracts;

namespace Shuffull.Api.Controllers;

/// <summary>
/// Song requests (Shuffull#57): a user asks for a song the library doesn't have, and the producer fetches it. The same
/// durable row + poll + ack shape as <see cref="YoutubeRatingsController"/>.
/// <list type="bullet">
/// <item>The app (signed in, any user) creates, lists and cancels its own requests. A request for a song the library
/// already has completes at once: the song is linked to the user and their weekly requests playlist, no producer.</item>
/// <item>The producer (shared secret) polls <c>?status=Pending</c>, acks its progress conditionally, and completes a
/// request by importing the song with <c>SongImportDetails.SongRequestId</c> (<see cref="SongRequestCompletion"/>).</item>
/// </list>
/// </summary>
[ApiController]
[Route("api/v1/song-requests")]
public class SongRequestsController : ControllerBase
{
    private readonly IMediator _mediator;

    public SongRequestsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>App: ask for a song by its source id. See <see cref="CreateSongRequestHandler"/>.</summary>
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Create([FromBody] CreateSongRequestRequest request, CancellationToken cancellationToken)
    {
        if (HttpContext.Items["User"] is not User user)
        {
            return Unauthorized();
        }

        return this.ToActionResult(await _mediator.Send(new CreateSongRequestCommand(user.UserId, request.ExternalSongId,
            request.Title, request.Artists, request.DurationSeconds, request.ThumbnailUrl), cancellationToken));
    }

    /// <summary>App: the caller's most recent requests, newest first, so it can show their status.</summary>
    [HttpGet]
    [Authorize]
    [QueryParameter("status", present: false)]
    public async Task<IActionResult> GetMine(CancellationToken cancellationToken)
    {
        if (HttpContext.Items["User"] is not User user)
        {
            return Unauthorized();
        }

        return this.ToActionResult(await _mediator.Send(new GetMySongRequestsQuery(user.UserId), cancellationToken));
    }

    /// <summary>App: cancel a request the producer hasn't claimed yet. Returns it as it now is.</summary>
    [HttpDelete("{songRequestId}")]
    [Authorize]
    public async Task<IActionResult> Cancel(string songRequestId, CancellationToken cancellationToken)
    {
        if (HttpContext.Items["User"] is not User user)
        {
            return Unauthorized();
        }

        return this.ToActionResult(await _mediator.Send(new CancelSongRequestCommand(user.UserId, songRequestId), cancellationToken));
    }

    /// <summary>Producer: the requests at <paramref name="status"/> (the funnel asks for Pending), oldest first.</summary>
    [HttpGet]
    [ProducerKey]
    [QueryParameter("status")]
    public async Task<IActionResult> GetQueue([FromQuery] SongRequestStatus status, CancellationToken cancellationToken)
        => this.ToActionResult(await _mediator.Send(new GetSongRequestQueueQuery(status), cancellationToken));

    /// <summary>Producer: report progress on each request. See <see cref="AckSongRequestsHandler"/>.</summary>
    [HttpPost("ack")]
    [ProducerKey]
    public async Task<IActionResult> Ack([FromBody] SongRequestAckDto[] acks, CancellationToken cancellationToken)
        => this.ToActionResult(await _mediator.Send(new AckSongRequestsCommand(acks ?? []), cancellationToken));
}
