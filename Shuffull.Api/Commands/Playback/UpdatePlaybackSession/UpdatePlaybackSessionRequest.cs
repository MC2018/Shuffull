using System.Text.Json;

namespace Shuffull.Api.Commands.Playback.UpdatePlaybackSession;

/// <summary>
/// Body for a device's own playback state (Shuffull#6). <paramref name="Scope"/> is the app's song filters, any JSON
/// object, stored and returned as sent. <paramref name="History"/> is the last songs played, newest last, and
/// <paramref name="QueuedNext"/> the play-next list; either left out counts as empty.
/// </summary>
public record UpdatePlaybackSessionRequest(
    string? SongId,
    double PositionSeconds,
    bool IsPlaying,
    JsonElement? Scope,
    List<string>? History,
    List<string>? QueuedNext);
