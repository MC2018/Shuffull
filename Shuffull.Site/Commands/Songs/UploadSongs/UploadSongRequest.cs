namespace Shuffull.Site.Commands.Songs.UploadSongs;

public record UploadSongRequest(string Username, string? PlaylistName, IEnumerable<IFormFile> Files);
