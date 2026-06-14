using Shuffull.Metadata.Enums;

namespace Shuffull.Metadata.Contracts;

/// <summary>
/// Canonical wire contract for a single song handed off from an external producer (the YoutubeFunnel)
/// to Shuffull for import. It is serialized to JSON and dropped into the external-import folder next to
/// the audio file; Shuffull's ExternalSongImporterService deserializes it into a SongImport row.
///
/// This is the single shared definition both sides are meant to reference, replacing the previously
/// hand-copied SongImportDetails (Shuffull) / SongExportDetails (funnel) record pair that could silently
/// drift. The funnel will be repointed at this type in a later, separately-coordinated step.
/// </summary>
[Serializable]
public record SongImportDetails(string ExternalSongId, string FileExtension, string ExternalPlaylistId, string TargetUserId, string? TargetPlaylistId, string? TargetPlaylistName, bool LikelyOriginalArtist, ExternalSource ExternalSource);
