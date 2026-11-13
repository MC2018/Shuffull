using System.Text.Json.Serialization;

namespace Shuffull.Core.Features.Songs.UpdateSongLyrics;

/// <summary>
/// <c>{"updated":true,"songId":…}</c> when the lyrics were stored, <c>{"updated":false,"reason":…}</c> when the song
/// was left alone. The unused field is left out of the JSON rather than sent as null.
/// </summary>
public record UpdateSongLyricsResponse(
    bool Updated,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SongId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason = null)
{
    public static UpdateSongLyricsResponse Stored(string songId) => new(true, SongId: songId);

    public static UpdateSongLyricsResponse Skipped(string reason) => new(false, Reason: reason);
}
