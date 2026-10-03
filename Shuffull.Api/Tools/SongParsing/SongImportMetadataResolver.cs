using Newtonsoft.Json;

namespace Shuffull.Api.Tools.SongParsing;

/// <summary>
/// Pure precedence rules for resolving a song's display name and artist list at import time. Both the
/// intake path (<see cref="SongImportIntakeService"/>) and the importer (<see cref="SongImportService"/>)
/// read tags off a TagLib file, but the actual "which source wins" decision is value-only and lives here so
/// it can be unit-tested without a real audio file.
/// </summary>
public static class SongImportMetadataResolver
{
    /// <summary>
    /// Resolves the song's display name. The producer's vetted title wins when present (it avoids inheriting a
    /// MusicBrainz tag-override the producer may have written into the file's ID3); otherwise the file's own
    /// ID3 title is used, falling back to the external id when neither is available.
    /// </summary>
    /// <param name="producerName">The producer-supplied <c>details.Name</c>; null/blank when not provided.</param>
    /// <param name="id3Title">The audio file's ID3 title; null/blank when absent or unparsable.</param>
    /// <param name="externalSongId">Final fallback identifier (never used as a name when a better source exists).</param>
    public static string ResolveSongName(string? producerName, string? id3Title, string externalSongId)
    {
        if (!string.IsNullOrWhiteSpace(producerName))
        {
            return producerName;
        }

        return string.IsNullOrWhiteSpace(id3Title) ? externalSongId : id3Title;
    }

    /// <summary>
    /// Resolves the ordered artist names. The producer's vetted artist list (carried as
    /// <see cref="Core.Models.Database.SongImport.ArtistsJson"/>) is authoritative whenever it is present (non-blank
    /// JSON) — it avoids a producer-side MusicBrainz match collapsing a multi-artist collab into one credit string.
    /// Blank entries are dropped from the producer list. Only a missing/blank <paramref name="artistsJson"/> falls
    /// back to the file's ID3 performers (manual uploads / older payloads that didn't carry artists).
    /// </summary>
    /// <param name="artistsJson">JSON-serialized <c>List&lt;string&gt;</c> of producer artists; null/blank => use ID3.</param>
    /// <param name="id3Performers">The audio file's ID3 performers; used when no producer list was supplied.</param>
    public static string[] ResolveArtistNames(string? artistsJson, string[] id3Performers)
    {
        if (!string.IsNullOrWhiteSpace(artistsJson))
        {
            return (JsonConvert.DeserializeObject<List<string>>(artistsJson) ?? new List<string>())
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .ToArray();
        }

        // The ID3 fallback is filtered too, so a blank performer entry can never become an empty-named artist.
        return (id3Performers ?? Array.Empty<string>())
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .ToArray();
    }
}
