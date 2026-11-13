using MediatR;
using Nut.Results;
using Shuffull.Core.Models.Dtos;

namespace Shuffull.Core.Features.Playback.SendPlaybackCommand;

/// <summary>
/// A remote control press for <paramref name="DeviceId"/> (Shuffull#6): stores it on the device's session as its one
/// pending command, replacing any it hasn't acked, and signals it. The device carries it out and acks it by
/// <paramref name="CommandId"/>. Scoped to <paramref name="UserId"/>.
/// </summary>
/// <param name="CommandId">The sender's id for this press (a UUID).</param>
/// <param name="Kind">One of <c>PlaybackCommandKinds</c>.</param>
/// <param name="SeekSeconds">Where to seek to; only for a seek, and required for one.</param>
public record SendPlaybackCommandCommand(string UserId, string DeviceId, string? CommandId, string? Kind, double? SeekSeconds)
    : IRequest<Result<PlaybackSessionDto>>;
