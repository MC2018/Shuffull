using Nut.Results;

namespace Shuffull.Core.Services;

/// <summary>
/// Durably records the user's decision to KEEP an audition song: clears <c>Song.Exploratory</c> and bumps
/// <c>Song.Version</c> so clients pick the change up on their next sync.
///
/// <para>This is deliberately its own operation, run BEFORE any enrichment, because the decision belongs to
/// the user and has to survive the engine failing, the AI being disabled, or the producer being unreachable.
/// Clearing the flag only as a side effect of a successful AI call — the previous design — meant a Keep left
/// no trace whenever the engine didn't run, and <c>DeletePlaylistHandler</c> later purged those songs (row +
/// media) as "never kept". That cost 34 songs on 2026-08-22; see the hub CLAUDE.md, "The audition ladder".</para>
///
/// <para>A promoted-but-untagged song stays DISCOVERABLE rather than being re-queued: it matches the
/// RetagStaleSongs work query (<c>!Exploratory &amp;&amp; TagModel == null</c>), so any sweep picks it up. Note
/// that sweep is currently only triggered by hand from the curator screen — there is no background job — so
/// what this guarantees is that the song survives and can be found, NOT that it gets re-tagged on its own.
/// The state being the work queue is the point: there is nothing in transit left to lose.</para>
///
/// <para>Defined in Core so MediatR handlers can drive it; implemented in Shuffull.Api, which owns the
/// DbContext — mirroring <see cref="ISongEnrichmentService"/>.</para>
/// </summary>
public interface IAuditionPromotionService
{
    /// <summary>
    /// Promotes the given songs out of audition. Idempotent — already-promoted songs are left untouched (so
    /// their <c>Version</c> doesn't churn) and unknown ids are ignored. Returns how many rows actually changed.
    /// </summary>
    Task<Result<int>> PromoteAsync(IReadOnlyList<string> songIds, CancellationToken cancellationToken = default!);
}
