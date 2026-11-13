using System.Text.Encodings.Web;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Shuffull.Api.Commands.Playback.SendPlaybackCommand;
using Shuffull.Api.Commands.Playback.TakeOverPlaybackSession;
using Shuffull.Api.Commands.Playback.UpdatePlaybackSession;
using Shuffull.Api.Extensions;
using Shuffull.Api.Tools.Authorization;
using Shuffull.Core.Features.Playback.AckPlaybackCommand;
using Shuffull.Core.Features.Playback.GetPlaybackSessions;
using Shuffull.Core.Features.Playback.SendPlaybackCommand;
using Shuffull.Core.Features.Playback.TakeOverPlaybackSession;
using Shuffull.Core.Features.Playback.UpdatePlaybackSession;
using Shuffull.Core.Models.Database;

namespace Shuffull.Api.Controllers;

/// <summary>
/// Cross-device playback (Shuffull#6): what each of the caller's devices is playing, handing playback from one to
/// another, and remote commands. Every route is scoped to the signed-in user: a device id that isn't one of theirs is a
/// 404. After each write the user's other devices get a "session changed" signal on <c>/hubs/playback</c>.
/// </summary>
[ApiController]
[Route("api/v1/playback/sessions")]
public class PlaybackController : ControllerBase
{
    /// <summary>Compact, with non-ASCII text kept as is rather than as <c>\uXXXX</c>, so a scope naming Japanese tags
    /// costs its real length against <see cref="PlaybackSession.MaxScopeLength"/>. The API escapes again on the way out.</summary>
    private static readonly JsonSerializerOptions ScopeJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly IMediator _mediator;

    public PlaybackController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// The caller's devices active in the last 30 days, most recent first: name, platform, last activity, whether it is
    /// connected to the hub, and its session (with the position estimated as of <c>serverTime</c>) if it has one.
    /// </summary>
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> List([FromCurrentUser] User user, CancellationToken cancellationToken)
    {
        return this.ToActionResult(await _mediator.Send(new GetPlaybackSessionsQuery(user.UserId), cancellationToken));
    }

    /// <summary>
    /// A device writes its own state: song, position, playing, scope, history and play-next list. Last write wins; the
    /// server stamps the time. The device must be checked in first (404 otherwise). Returns the session, which also
    /// carries any takeover or pending command for this device.
    /// </summary>
    [HttpPut("{deviceId}")]
    [Authorize]
    public async Task<IActionResult> Update([FromCurrentUser] User user, string deviceId, [FromBody] UpdatePlaybackSessionRequest request,
        CancellationToken cancellationToken)
    {
        var scope = request.Scope is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } element
            ? JsonSerializer.Serialize(element, ScopeJson)
            : null;
        return this.ToActionResult(await _mediator.Send(new UpdatePlaybackSessionCommand(
            user.UserId, deviceId, request.SongId, request.PositionSeconds, request.IsPlaying, scope,
            request.History ?? [], request.QueuedNext ?? []), cancellationToken));
    }

    /// <summary>
    /// "Play here": <c>byDeviceId</c> takes over from <c>{deviceId}</c>. Pauses the source at its estimated position
    /// and records the takeover, only if the source's <c>updatedAt</c> still equals <c>expectedUpdatedAt</c>; otherwise
    /// 409, and the caller re-reads. Returns the source session as it now is: the position to resume from.
    /// </summary>
    [HttpPost("{deviceId}/takeover")]
    [Authorize]
    public async Task<IActionResult> TakeOver([FromCurrentUser] User user, string deviceId, [FromBody] TakeOverPlaybackSessionRequest request,
        CancellationToken cancellationToken)
    {
        return this.ToActionResult(await _mediator.Send(
            new TakeOverPlaybackSessionCommand(user.UserId, deviceId, request.ByDeviceId, request.ExpectedUpdatedAt), cancellationToken));
    }

    /// <summary>
    /// A remote control press for <c>{deviceId}</c>: play, pause, next, previous or seek. Stored as the device's one
    /// pending command (replacing any unacked one) until the device acks it. 404 if the device has no session.
    /// </summary>
    [HttpPost("{deviceId}/command")]
    [Authorize]
    public async Task<IActionResult> SendCommand([FromCurrentUser] User user, string deviceId, [FromBody] SendPlaybackCommandRequest request,
        CancellationToken cancellationToken)
    {
        return this.ToActionResult(await _mediator.Send(
            new SendPlaybackCommandCommand(user.UserId, deviceId, request.CommandId, request.Kind, request.SeekSeconds), cancellationToken));
    }

    /// <summary>
    /// The device carried out <c>{commandId}</c>: clears it, only if it is still the pending command (409 otherwise,
    /// so a newer command is never lost).
    /// </summary>
    [HttpPost("{deviceId}/command/{commandId}/ack")]
    [Authorize]
    public async Task<IActionResult> AckCommand([FromCurrentUser] User user, string deviceId, string commandId,
        CancellationToken cancellationToken)
    {
        return this.ToActionResult(await _mediator.Send(new AckPlaybackCommandCommand(user.UserId, deviceId, commandId), cancellationToken));
    }
}
