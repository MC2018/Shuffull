namespace Shuffull.Core.Features.Playlists.AddSongToPlaylist;

/// <summary>
/// Result of adding a song to a playlist. <see cref="Added"/> is false when the song was already on
/// the playlist (a no-op), so the operation is idempotent.
/// </summary>
public record AddSongToPlaylistResponse(bool Added);
