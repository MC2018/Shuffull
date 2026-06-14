using Shuffull.Metadata.Enums;
using Shuffull.Metadata.Models;

namespace Shuffull.Metadata.Contracts;

/// <summary>
/// Canonical wire contract for a single song handed off from an external producer (the YoutubeFunnel)
/// to Shuffull for import. It is serialized to JSON and dropped into the external-import folder next to
/// the audio file; Shuffull's ExternalSongImporterService deserializes it into a SongImport row.
///
/// This is the single shared definition both sides are meant to reference, replacing the previously
/// hand-copied SongImportDetails (Shuffull) / SongExportDetails (funnel) record pair that could silently
/// drift. The funnel will be repointed at this type in a later, separately-coordinated step.
///
/// <para><see cref="GeneratedTags"/> is optional. When the producer (the funnel, with its richer
/// per-video context) has already inferred genres/era/languages, it may attach them here so Shuffull can
/// persist them directly and skip its own AI call. When null — e.g. manual uploads or producers that do
/// not generate tags — Shuffull falls back to generating them itself. Older payloads that omit the field
/// deserialize to null, so this addition is backward-compatible.</para>
/// </summary>
[Serializable]
public record SongImportDetails(
    string ExternalSongId,
    string FileExtension,
    string ExternalPlaylistId,
    string TargetUserId,
    string? TargetPlaylistId,
    string? TargetPlaylistName,
    bool LikelyOriginalArtist,
    ExternalSource ExternalSource,
    GeneratedSongTags? GeneratedTags = null);
