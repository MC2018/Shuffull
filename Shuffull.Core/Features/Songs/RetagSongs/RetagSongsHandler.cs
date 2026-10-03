using MediatR;
using Nut.Results;
using Shuffull.Core.Services;

namespace Shuffull.Core.Features.Songs.RetagSongs;

/// <summary>
/// Re-tags each requested song via the enrichment service - each in its own scope/transaction, so one failure
/// doesn't roll back the rest. De-dupes ids and bounds the batch (the outbox chunks larger sets). Authorization
/// is upstream.
///
/// <para>Every id here was Kept or Liked by the user, so the batch FIRST promotes them all out of audition
/// (<see cref="IAuditionPromotionService"/>) and only then attempts enrichment. The two steps are separate on
/// purpose: the decision is durable even when the engine is disabled or failing, which is exactly the case
/// that silently destroyed kept songs before. See the hub CLAUDE.md, "The audition ladder".</para>
/// </summary>
public class RetagSongsHandler(IAuditionPromotionService promotion, ISongEnrichmentService enrichment) : IRequestHandler<RetagSongsCommand, Result<RetagSongsResponse>>
{
    // Bound a single request; each id is an AI call, so the caller (outbox) chunks larger sets.
    public const int MaxBatch = 200;

    public async Task<Result<RetagSongsResponse>> Handle(RetagSongsCommand request, CancellationToken cancellationToken)
    {
        // Collapse duplicates in first-occurrence order with STRONGER-WINS (mirrors the app outbox's upgrade
        // rule: a Like queued after a Keep flips the pending re-tag to strong, never the reverse).
        var modelById = new Dictionary<string, string?>();
        var order = new List<string>();
        foreach (var item in request.Items ?? [])
        {
            if (item is null || string.IsNullOrWhiteSpace(item.SongId))
            {
                continue;
            }

            if (!modelById.TryGetValue(item.SongId, out var existing))
            {
                modelById[item.SongId] = item.Model;
                order.Add(item.SongId);
            }
            else if (Rank(item.Model) > Rank(existing))
            {
                modelById[item.SongId] = item.Model;
            }
        }

        if (order.Count == 0)
        {
            return Result.Ok(new RetagSongsResponse([]));
        }
        if (order.Count > MaxBatch)
        {
            return Result.Error<RetagSongsResponse>($"Too many songs in one request ({order.Count}); max {MaxBatch}.");
        }

        // Record intent BEFORE spending (or failing to spend) any AI. Promotion covers EVERY id in the batch,
        // including one whose model tier fails to parse below: a client-side typo in the tier says nothing
        // about whether the user kept the song, and intent must not hinge on the rest of the pipeline working.
        //
        // A failure here aborts the batch rather than enriching anyway. The caller (the app's outbox) then
        // keeps its rows and retries, which is the whole point — dropping the decision is the failure mode we
        // are removing.
        var promoteResult = await promotion.PromoteAsync(order, cancellationToken);
        if (promoteResult.IsError)
        {
            return Result.Error<RetagSongsResponse>(
                $"Could not record the keep/promote for this batch; no songs were re-tagged. {promoteResult.GetError().Message}");
        }

        var results = new List<SongRetagResult>(order.Count);
        foreach (var songId in order)
        {
            // Wire tier -> engine model, per item. An unknown value fails only ITS item — never silently
            // upgraded (a typo'd "weka" must not spend strong-model money), never sinking the rest of a
            // mixed outbox flush.
            var model = TryResolveModel(modelById[songId]);
            if (model is null)
            {
                results.Add(new SongRetagResult(songId, Outcomes.Failed,
                    $"Unknown model tier '{modelById[songId]}'; expected '{RetagModels.Weak}' or '{RetagModels.Strong}'."));
                continue;
            }

            var result = await enrichment.EnrichSongAsync(songId, model.Value, cancellationToken);
            if (result.IsError)
            {
                results.Add(new SongRetagResult(songId, Outcomes.Failed, result.GetError().Message));
            }
            else
            {
                results.Add(new SongRetagResult(songId,
                    result.Get() == SongEnrichmentStatus.SkippedMetadataLocked ? Outcomes.Skipped : Outcomes.Enriched));
            }
        }

        return Result.Ok(new RetagSongsResponse(results));
    }

    private static EnrichmentModel? TryResolveModel(string? model) => model?.Trim().ToLowerInvariant() switch
    {
        null or "" or RetagModels.Strong => EnrichmentModel.Strong,
        RetagModels.Weak => EnrichmentModel.Weak,
        _ => null,
    };

    /// <summary>Collapse ranking: strong (incl. null default) beats weak beats unrecognized.</summary>
    private static int Rank(string? model) => TryResolveModel(model) switch
    {
        EnrichmentModel.Strong => 2,
        EnrichmentModel.Weak => 1,
        _ => 0,
    };
}
