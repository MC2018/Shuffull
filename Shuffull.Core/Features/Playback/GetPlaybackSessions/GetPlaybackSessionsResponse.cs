using Shuffull.Core.Models.Dtos;

namespace Shuffull.Core.Features.Playback.GetPlaybackSessions;

/// <param name="ServerTime">The server's clock when it answered. Every <c>EstimatedPositionSeconds</c> is as of this
/// moment, so a client can carry the estimate forward on its own clock without trusting the two clocks to agree.</param>
public record GetPlaybackSessionsResponse(DateTime ServerTime, IReadOnlyList<PlaybackDeviceDto> Devices);
