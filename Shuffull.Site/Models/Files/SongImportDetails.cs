using Shuffull.Site.Models.Enums;

namespace Shuffull.Site.Models.Files;

[Serializable]
public record SongImportDetails(string ExternalSongId, string FileExtension, string ExternalPlaylistId, string TargetUserId, string? TargetPlaylistId, string? TargetPlaylistName, bool LikelyOriginalArtist, ExternalSource ExternalSource);
