namespace Shuffull.Core.Features.Playlists.RemoveSongFromPlaylist;

/// <summary>
/// Result of removing a song from a playlist. <see cref="Removed"/> is false when the song was not on
/// the playlist (a no-op), so the operation is idempotent.
/// </summary>
public record RemoveSongFromPlaylistResponse(bool Removed);
