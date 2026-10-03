namespace Shuffull.Core.Features.Songs.KeepSongs;

/// <summary>
/// Per-song outcome, same wire shape as the re-tag results so the app's outbox reads both the same way.
/// <see cref="Outcome"/> is one of <see cref="KeepOutcomes"/>.
/// </summary>
public record SongKeepResult(string SongId, string Outcome, string? Error = null);

/// <summary>The stable outcome strings on the wire (enums would serialize as ints here).</summary>
public static class KeepOutcomes
{
    public const string Kept = "kept"; // promoted now, or already promoted — Keep is idempotent
    public const string Failed = "failed";
}

/// <summary>The per-song results for the batch, in request order (after de-dup).</summary>
public record KeepSongsResponse(IReadOnlyList<SongKeepResult> Results);
