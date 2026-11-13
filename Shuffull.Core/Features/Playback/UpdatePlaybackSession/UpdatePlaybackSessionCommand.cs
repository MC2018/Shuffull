using MediatR;
using Nut.Results;
using Shuffull.Core.Models.Dtos;

namespace Shuffull.Core.Features.Playback.UpdatePlaybackSession;

/// <summary>
/// A device writing its own playback state (Shuffull#6): creates its session row the first time, then overwrites it.
/// Last write wins. The server stamps the position with its own clock. Scoped to <paramref name="UserId"/>, and the
/// device must already be checked in as one of that user's devices.
/// </summary>
/// <param name="Scope">The app's song filters as compact JSON (an object), or null.</param>
/// <param name="History">The last songs played, newest last.</param>
/// <param name="QueuedNext">The play-next songs, in order.</param>
public record UpdatePlaybackSessionCommand(
    string UserId,
    string DeviceId,
    string? SongId,
    double PositionSeconds,
    bool IsPlaying,
    string? Scope,
    IReadOnlyList<string> History,
    IReadOnlyList<string> QueuedNext) : IRequest<Result<PlaybackSessionDto>>;
