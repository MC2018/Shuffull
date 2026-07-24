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
/// Which engine model an enrichment runs. <see cref="Strong"/> is the full-quality default (likes, curator
/// upgrades); <see cref="Weak"/> is the budget tier used when the user KEEPS an audition song without liking
/// it — the song gets real tags cheaply, and its weak <c>TagModel</c> leaves it upgradeable later (the
/// staleness sweep / a like re-tags it strong).
/// </summary>
public enum EnrichmentModel
{
    Strong = 0,
    Weak = 1,
}

/// <summary>
/// Re-tags a single song from its already-stored inputs (name, artists, lyrics, measured BPM, audio-shape
/// features, authoritative release year) with the current strong model - no re-download. Defined in Core so
/// MediatR handlers (the re-tag commands) can drive it; the implementation lives in Shuffull.Api, which owns
/// the genre engine + the tag-entity mapping.
/// </summary>
public interface ISongEnrichmentService
{
    Task<Result<SongEnrichmentStatus>> EnrichSongAsync(string songId, EnrichmentModel model = EnrichmentModel.Strong, CancellationToken cancellationToken = default!);
}
