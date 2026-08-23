using MediatR;
using Nut.Results;

namespace Shuffull.Core.Features.Songs.GetPendingTagSongs;

/// <summary>
/// Producer-facing work queue: the songs whose tags are behind the model they deserve, oldest first.
///
/// <para>There is no queue table. The work item IS the state — a song qualifies purely because of its own
/// columns — so this is self-healing and idempotent: a failed tagging run simply matches again next cycle, and
/// there is no watermark or in-flight row to lose. Same property as RetagStaleSongs, and the reason the funnel
/// can be restarted, redeployed or disconnected without dropping anything.</para>
///
/// <para>The TIER is DERIVED, never stored: a song positively rated by any user earns the strong model,
/// everything else the weak one. Deriving matters — a stored "strong" request from a like the user immediately
/// undid would linger and spend strong-model money on a song they no longer care about. Reading current state
/// self-corrects, and a Keep that is later Loved upgrades on the next poll with nothing to migrate.</para>
/// </summary>
public record GetPendingTagSongsQuery(int Limit) : IRequest<Result<PendingTagSongsResponse>>;

/// <summary>The engine tier a song should be tagged with, derived from current sentiment.</summary>
public static class TagTiers
{
    public const string Weak = "weak";
    public const string Strong = "strong";
}

/// <summary>
/// One song to tag, carrying the inputs the engine needs so the producer never has to call back for them.
/// Mirrors what SongEnrichmentService feeds the genre engine: identity, lyrics, and the measured audio shape.
/// Legacy songs predate provenance capture and carry nulls here; the engine then works from name/artists alone.
/// </summary>
public record PendingTagSong(
    string SongId,
    /// <summary>
    /// The source YouTube video id, when the song came from the funnel. Passed back so the producer can key
    /// its AI-response cache on the SAME id it used at ingest — a song it already tagged can then be re-tagged
    /// from cache instead of paying the engine again. Null for manual uploads.
    /// </summary>
    string? ExternalSongId,
    string Name,
    IReadOnlyList<string> Artists,
    string Tier,
    string? CurrentTagModel,
    string? PlainLyrics,
    string? SyncedLyrics,
    bool LyricsInstrumental,
    int? MeasuredBpm,
    double? CrestFactorDb,
    double? LoudnessRangeLu,
    double? OnsetsPerSecond,
    int? OriginalReleaseYear);

/// <summary><see cref="Remaining"/> is the total still outstanding, so the producer can pace itself.</summary>
public record PendingTagSongsResponse(IReadOnlyList<PendingTagSong> Songs, int Remaining);
