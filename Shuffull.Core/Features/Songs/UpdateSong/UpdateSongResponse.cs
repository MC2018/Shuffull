namespace Shuffull.Core.Features.Songs.UpdateSong;

/// <summary>Confirms the edit and returns the song's new sync version (its updated <c>Song.Version</c>).</summary>
public record UpdateSongResponse(string SongId, DateTime Version);
