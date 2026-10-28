namespace Shuffull.Api.Commands.Songs.UploadSongs;

public record UploadSongRequest(string UserId, string? PlaylistName, IEnumerable<IFormFile> Files);
