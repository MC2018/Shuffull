namespace Shuffull.Core.Features.Playlists.DeletePlaylist;

/// <summary>
/// Result of deleting a playlist. <see cref="Deleted"/> is false when no matching playlist existed for the user
/// (an idempotent no-op, e.g. a retried offline delete). <see cref="PurgedSongIds"/> lists any un-kept audition
/// songs removed alongside an exploratory playlist — empty for a normal playlist.
/// </summary>
public record DeletePlaylistResponse(string PlaylistId, bool Deleted, IReadOnlyList<string> PurgedSongIds);
