namespace Shuffull.Api.Commands.SongRequests;

/// <summary>
/// The app asking for a song by its source id, with what it showed the user from the search result. The title and
/// artists are only for display and for the producer; the song itself is identified by <see cref="ExternalSongId"/>.
/// </summary>
public record CreateSongRequestRequest(
    string ExternalSongId,
    string Title,
    List<string>? Artists,
    int? DurationSeconds,
    string? ThumbnailUrl);
