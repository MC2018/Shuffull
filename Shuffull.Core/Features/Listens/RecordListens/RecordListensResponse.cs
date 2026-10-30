namespace Shuffull.Core.Features.Listens.RecordListens;

/// <summary>
/// Per-listen outcome, the same shape as the Keep and re-tag results so the app's outbox reads them all the same
/// way. <see cref="Outcome"/> is one of <see cref="ListenOutcomes"/>; <see cref="Error"/> says why a listen was
/// rejected.
/// </summary>
public record ListenResult(string? ListenId, string Outcome, string? Error = null);

/// <summary>The stable outcome strings on the wire (enums would serialize as ints here).</summary>
public static class ListenOutcomes
{
    public const string Recorded = "recorded";
    /// <summary>Already recorded: a resend, or the same listen twice in one batch. Nothing was written.</summary>
    public const string Duplicate = "duplicate";
    /// <summary>Not recorded, and a resend won't change that: the item is invalid or isn't the caller's.</summary>
    public const string Rejected = "rejected";
}

/// <summary>One result per item, in request order.</summary>
public record RecordListensResponse(IReadOnlyList<ListenResult> Results);
