using MediatR;
using Nut.Results;
using Shuffull.Core.Authentication;

namespace Shuffull.Core.Features.Songs.RetagSongs;

/// <summary>
/// Curator-only: force re-tag a specific set of songs from their stored inputs (no re-download), regardless
/// of staleness. PER-ITEM model tiers so the app's offline outbox can flush a MIXED backlog (audition Keeps
/// queued weak alongside like-promotions queued strong) in a single burst call. Duplicate ids collapse with
/// stronger-wins — a Like queued after a Keep must not be downgraded by the older weak row. Curator-locked
/// songs are reported skipped, not touched. For a model-driven library sweep, use RetagStaleSongs instead.
/// </summary>
[RequiresRole(Role.Curator)]
public record RetagSongsCommand(IReadOnlyList<SongRetagItem> Items) : IRequest<Result<RetagSongsResponse>>;

/// <summary>One song to re-tag and the engine tier to run: <c>"weak"</c> (budget — an audition Keep) or
/// <c>"strong"</c>/null (full quality — likes, curator upgrades).</summary>
public record SongRetagItem(string SongId, string? Model = null);

/// <summary>The stable model-tier strings on the wire.</summary>
public static class RetagModels
{
    public const string Strong = "strong";
    public const string Weak = "weak";
}

/// <summary>
/// Per-song outcome so the caller (and the outbox) can mark items done or retry only the failures.
/// <see cref="Outcome"/> is a self-describing wire string - one of <see cref="Outcomes"/>.
/// </summary>
public record SongRetagResult(string SongId, string Outcome, string? Error = null);

/// <summary>The stable outcome strings on the wire (enums would serialize as ints here).</summary>
public static class Outcomes
{
    public const string Enriched = "enriched";
    public const string Skipped = "skipped"; // curator-locked; left untouched
    public const string Failed = "failed";
}

/// <summary>The per-song results for the batch, in request order (after de-dup).</summary>
public record RetagSongsResponse(IReadOnlyList<SongRetagResult> Results);
