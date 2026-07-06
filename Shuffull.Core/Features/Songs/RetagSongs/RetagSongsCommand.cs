using MediatR;
using Nut.Results;
using Shuffull.Core.Authentication;

namespace Shuffull.Core.Features.Songs.RetagSongs;

/// <summary>
/// Curator-only: force re-tag a specific set of songs from their stored inputs with the current strong model
/// (no re-download), regardless of staleness. Multi-id so the app's offline outbox can coalesce many queued
/// re-tags into a single replay call (and to power exploratory "promote on like"). Curator-locked songs are
/// reported skipped, not touched. For a model-driven sweep of the whole library, use RetagStaleSongs instead.
/// </summary>
[RequiresRole(Role.Curator)]
public record RetagSongsCommand(IReadOnlyList<string> SongIds) : IRequest<Result<RetagSongsResponse>>;

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
