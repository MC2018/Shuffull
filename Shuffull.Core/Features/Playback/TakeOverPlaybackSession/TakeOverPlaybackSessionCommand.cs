using MediatR;
using Nut.Results;
using Shuffull.Core.Models.Dtos;

namespace Shuffull.Core.Features.Playback.TakeOverPlaybackSession;

/// <summary>
/// "Play here": <paramref name="ByDeviceId"/> takes playback over from <paramref name="DeviceId"/> (Shuffull#6). Pauses
/// the source's row at its estimated position and records who took over and when, but only if the row is still the one
/// the taker read (<paramref name="ExpectedUpdatedAt"/>). Both devices must be the caller's.
/// </summary>
/// <param name="ExpectedUpdatedAt">The source session's <c>UpdatedAt</c> as the taker read it.</param>
public record TakeOverPlaybackSessionCommand(string UserId, string DeviceId, string? ByDeviceId, DateTime? ExpectedUpdatedAt)
    : IRequest<Result<PlaybackSessionDto>>;
