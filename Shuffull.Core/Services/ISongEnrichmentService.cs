using Nut.Results;

namespace Shuffull.Core.Services;

/// <summary>Outcome of an enrichment attempt that didn't error.</summary>
public enum SongEnrichmentStatus
{
    /// <summary>Tags/BPM/energy were regenerated and written in place.</summary>
    Enriched,
    /// <summary>The song is curator-locked; nothing was touched.</summary>
    SkippedMetadataLocked,
}

/// <summary>
/// Re-tags a single song from its already-stored inputs (name, artists, lyrics, measured BPM, audio-shape
/// features, authoritative release year) with the current strong model - no re-download. Defined in Core so
/// MediatR handlers (the re-tag commands) can drive it; the implementation lives in Shuffull.Api, which owns
/// the genre engine + the tag-entity mapping.
/// </summary>
public interface ISongEnrichmentService
{
    Task<Result<SongEnrichmentStatus>> EnrichSongAsync(string songId, CancellationToken cancellationToken = default!);
}
