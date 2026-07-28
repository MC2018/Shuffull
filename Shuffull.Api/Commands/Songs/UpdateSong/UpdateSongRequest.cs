using Shuffull.Core.Features.Songs.UpdateSong;

namespace Shuffull.Api.Commands.Songs.UpdateSong;

/// <summary>Body for the curator song-metadata edit (the song id comes from the route).</summary>
public record UpdateSongRequest(
    string Name,
    int? Bpm,
    int? Energy,
    IReadOnlyList<string> Artists,
    IReadOnlyList<SongTagEdit> Tags);
