using MediatR;
using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence;
using Shuffull.Core.Tools;
using Shuffull.Core.Models.Files;
using Shuffull.Metadata.Tools;
using Shuffull.Shared.Tools;

namespace Shuffull.Core.Features.Songs.ApplySongTags;

/// <summary>
/// Applies producer-generated tags to an existing song, in one transaction. Authorization is upstream (the
/// producer's shared secret).
/// </summary>
public class ApplySongTagsHandler(ShuffullContext context, ModelStrengths modelStrengths)
    : IRequestHandler<ApplySongTagsCommand, Result<ApplySongTagsResponse>>
{
    public async Task<Result<ApplySongTagsResponse>> Handle(ApplySongTagsCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TagModel))
        {
            // Without provenance the song would still look untagged and come straight back on the next poll.
            return Result.Error<ApplySongTagsResponse>("TagModel is required; tags without provenance would re-queue forever.");
        }

        var song = await context.Songs
            .Include(s => s.SongTags)
            .FirstOrDefaultAsync(s => s.SongId == request.SongId, cancellationToken);
        if (song is null)
        {
            return Result.Error<ApplySongTagsResponse>(ResultErrors.NotFound($"Song '{request.SongId}' was not found."));
        }

        // Only a genuine upgrade is written, by the same rules the work query hands songs out by: a stronger model,
        // or a newer prompt revision from a model no weaker than the current one. Checked
        // here as well because the producer polls and writes independently: a song can be curated or re-tagged
        // in the window between, and the write is the last point that can still refuse. It also refuses a
        // producer whose model is no stronger than the song's, rather than rewriting the same song and bumping
        // its Version on every cycle (Shuffull#40). A refused song keeps its TagAttempts, so the queue keeps
        // backing it off.
        if (!TagUpgradeRule.WouldUpgrade(song, request.TagModel, modelStrengths)
            && !TagUpgradeRule.WouldRescore(song, request.TagModel, request.TagRevision, modelStrengths))
        {
            var locked = song.MetadataLocked ? " (curator-locked)" : "";
            return Result.Error<ApplySongTagsResponse>(
                $"Song '{request.SongId}'{locked}: '{request.TagModel}' (revision {request.TagRevision}) is not an upgrade on " +
                $"'{song.TagModel ?? "(hand-tagged)"}' (revision {song.TagRevision}).");
        }

        using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // Rewrite the joins wholesale, matching the enrichment path: a song carries one coherent set of
        // machine tags rather than an accumulation across models.
        var produced = request.Tags.ToTagList();
        var tags = await TagStore.GetOrCreateAsync(context, produced, cancellationToken);

        context.SongTags.RemoveRange(song.SongTags);
        await context.SaveChangesAsync(cancellationToken);

        foreach (var tag in tags)
        {
            context.SongTags.Add(new SongTag
            {
                SongTagId = IdGenerator.Generate(),
                SongId = song.SongId,
                TagId = tag.TagId,
            });
        }

        if (request.Tags.Heaviness is int heaviness)
        {
            song.Heaviness = heaviness;
        }
        else if (request.TagRevision > song.TagRevision)
        {
            // A newer revision with no score: the old value is on a scale that no longer applies, so unknown is
            // the honest answer. Leaving the revision behind instead would hand the song straight back forever.
            // A song the site enriched (stamped one behind, SongEnrichmentService.SiteTagRevision) loses a score on
            // the current scale this way, just without grit and density. Accepted: it only happens when the
            // producer's heaviness call fails, and it costs machine data, not a user decision.
            song.Heaviness = null;
        }

        song.TagModel = request.TagModel;
        // Taken as sent, even when lower: a model upgrade from a producer on an older prompt leaves the song due
        // for a re-score, which is true.
        song.TagRevision = request.TagRevision;
        song.ResetTagAttempts();

        // NOT set: Name, artists (curator territory) and MetadataLocked (a producer write is not a human
        // decision, and locking here would exclude the song from every future upgrade).
        song.Version = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Result.Ok(new ApplySongTagsResponse(song.SongId, produced.Count, request.TagModel));
    }
}
