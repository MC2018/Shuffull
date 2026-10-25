using System.Text.Json.Serialization;
using Shuffull.Core.Features.Songs.UpdateSong;

namespace Shuffull.Api.Commands.Songs.UpdateSong;

/// <summary>Body for the curator song-metadata edit (the song id comes from the route).</summary>
public record UpdateSongRequest(
    string Name,
    int? Bpm,
    int? Heaviness,
    IReadOnlyList<string> Artists,
    IReadOnlyList<SongTagEdit> Tags)
{
    /// <summary>
    /// Heaviness under its name before shuffull-workspace#50. App builds from before the rename send it, and so
    /// do edits they queued in their outbox, which a renamed app still sends as written. Without this the edit
    /// would land with no heaviness and clear the song's score. Read only.
    /// </summary>
    [JsonPropertyName("energy")]
    [JsonInclude]
    private int? LegacyEnergy
    {
        init => Heaviness ??= value;
    }
}
