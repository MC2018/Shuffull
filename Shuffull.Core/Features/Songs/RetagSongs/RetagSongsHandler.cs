using MediatR;
using Nut.Results;
using Shuffull.Core.Services;

namespace Shuffull.Core.Features.Songs.RetagSongs;

/// <summary>
/// Re-tags each requested song via the enrichment service - each in its own scope/transaction, so one failure
/// doesn't roll back the rest. De-dupes ids and bounds the batch (the outbox chunks larger sets). Authorization
/// is upstream.
/// </summary>
public class RetagSongsHandler(ISongEnrichmentService enrichment) : IRequestHandler<RetagSongsCommand, Result<RetagSongsResponse>>
{
    // Bound a single request; each id is an AI call, so the caller (outbox) chunks larger sets.
    public const int MaxBatch = 200;

    public async Task<Result<RetagSongsResponse>> Handle(RetagSongsCommand request, CancellationToken cancellationToken)
    {
        var ids = request.SongIds?
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .ToList() ?? [];

        if (ids.Count == 0)
        {
            return Result.Ok(new RetagSongsResponse([]));
        }
        if (ids.Count > MaxBatch)
        {
            return Result.Error<RetagSongsResponse>($"Too many songs in one request ({ids.Count}); max {MaxBatch}.");
        }

        var results = new List<SongRetagResult>(ids.Count);
        foreach (var songId in ids)
        {
            var result = await enrichment.EnrichSongAsync(songId, cancellationToken);
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
}
