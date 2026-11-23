using MediatR;
using Nut.Results;
using Shuffull.Core.Models.Dtos;

namespace Shuffull.Core.Features.Playback.AckPlaybackCommand;

/// <summary>
/// A device saying it carried out <paramref name="CommandId"/> (Shuffull#6): clears its pending command, but only if
/// that is still the pending one, so an ack can never clear a newer command it hasn't seen. Scoped to
/// <paramref name="UserId"/>.
/// </summary>
public record AckPlaybackCommandCommand(string UserId, string DeviceId, string CommandId) : IRequest<Result<PlaybackSessionDto>>;
