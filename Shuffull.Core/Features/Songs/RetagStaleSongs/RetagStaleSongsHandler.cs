using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Nut.Results;
using Shuffull.Core.Persistence;
using Shuffull.Core.Services;
using Shuffull.Core.Tools;

namespace Shuffull.Core.Features.Songs.RetagStaleSongs;

/// <summary>
/// Selects up to <c>Limit</c> stale, unlocked songs (oldest-version first) and re-tags each via the enrichment
/// service. Each enrichment runs in its own scope/transaction (inside the service), so one failure doesn't roll
/// back the rest. Authorization is upstream.
/// </summary>
public class RetagStaleSongsHandler(
    ShuffullContext context,
    ModelStrengths modelStrengths,
    IConfiguration configuration,
    ISongEnrichmentService enrichment)
    : IRequestHandler<RetagStaleSongsCommand, Result<RetagStaleSongsResponse>>
{
    // Guard rails so a single request can't be unbounded (each item is an AI call).
    private const int DefaultLimit = 25;
    private const int MaxLimit = 200;

    public async Task<Result<RetagStaleSongsResponse>> Handle(RetagStaleSongsCommand request, CancellationToken cancellationToken)
    {
        var limit = request.Limit <= 0 ? DefaultLimit : Math.Min(request.Limit, MaxLimit);

        // The strong model the engine tags toward - the same resolution the site's OpenAI config uses.
        var strongModel = configuration["AI:OpenAI:StrongModelName"];
        if (string.IsNullOrWhiteSpace(strongModel))
        {
            strongModel = configuration["AI:OpenAI:ModelName"];
        }

        // Fail-safe: if the current strong model has no registered strength (0), nothing is stale. Never let a
        // forgotten registration turn into a whole-library re-tag.
        if (modelStrengths.GetStrength(strongModel) <= 0)
        {
            return Result.Ok(new RetagStaleSongsResponse(0, 0, 0, strongModel));
        }

        // Stale = weaker (or null/unknown) TagModel, NOT curator-locked, and NOT exploratory (un-vetted audition
        // songs must not have AI spent on them until the user keeps one). Locked/exploratory are excluded so they
        // can't be perpetually re-selected. Expressed as SQL so we don't load the whole library.
        var strongEnough = modelStrengths.ModelsAtLeastAsStrongAs(strongModel).ToList();
        var staleQuery = context.Songs.Where(s =>
            !s.MetadataLocked && !s.Exploratory && (s.TagModel == null || !strongEnough.Contains(s.TagModel)));

        var totalStale = await staleQuery.CountAsync(cancellationToken);
        var batch = await staleQuery
            .OrderBy(s => s.Version)
            .Select(s => s.SongId)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var enriched = 0;
        var failed = 0;
        foreach (var songId in batch)
        {
            var result = await enrichment.EnrichSongAsync(songId, cancellationToken);
            if (result.IsError)
            {
                failed++;
            }
            else if (result.Get() == SongEnrichmentStatus.Enriched)
            {
                enriched++;
            }
            // SkippedMetadataLocked shouldn't occur (locked songs are filtered out), but if a race locks one
            // between selection and enrichment it's simply neither enriched nor failed - it drops out next pass.
        }

        // Songs that were successfully enriched are no longer stale; everything else still is.
        var remaining = totalStale - enriched;
        return Result.Ok(new RetagStaleSongsResponse(enriched, failed, remaining, strongModel));
    }
}
