using MediatR;
using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence;
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
            return Result.Error<ApplySongTagsResponse>($"Song '{request.SongId}' was not found.");
        }

        // A locked song only yields to a genuine MODEL upgrade. Guarded here as well as in the work query
        // because the producer polls and writes independently: a song can be curated in the window between,
        // and the write is the last point that can still refuse.
        // Note the null check is SEPARATE from the strength comparison, and both are required. GetStrength(null)
        // is 0, so a strength test alone would read "any model beats nothing" and let the weak model overwrite
        // a song a person tagged by hand — UpdateSong never stamps TagModel, so hand-tagged and never-tagged
        // look identical. A locked song with no prior model has nothing to upgrade FROM, so it is untouchable.
        // This mirrors the work query exactly; they must agree, and this is the last point that can refuse.
        if (song.MetadataLocked
            && (song.TagModel == null
                || modelStrengths.GetStrength(request.TagModel) <= modelStrengths.GetStrength(song.TagModel)))
        {
            return Result.Error<ApplySongTagsResponse>(
                $"Song '{request.SongId}' is curator-locked and '{request.TagModel}' is not an upgrade on '{song.TagModel ?? "(hand-tagged)"}'.");
        }

        using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // Rewrite the joins wholesale, matching the enrichment path: a song carries one coherent set of
        // machine tags rather than an accumulation across models.
        var produced = request.Tags.ToTagList();
        var knownTags = await context.Tags.ToListAsync(cancellationToken);
        var existingTags = knownTags.Where(x => produced.Any(y => y.Name == x.Name)).ToList();
        var newTags = produced.Where(x => knownTags.All(y => y.Name != x.Name)).ToList();

        context.SongTags.RemoveRange(song.SongTags);
        context.Tags.AddRange(newTags);
        await context.SaveChangesAsync(cancellationToken);

        foreach (var tag in existingTags.Concat(newTags))
        {
            context.SongTags.Add(new SongTag
            {
                SongTagId = IdGenerator.Generate(),
                SongId = song.SongId,
                TagId = tag.TagId,
            });
        }

        if (request.Tags.Energy is int energy)
        {
            song.Energy = energy;
        }

        song.TagModel = request.TagModel;

        // NOT set: Name, artists (curator territory) and MetadataLocked (a producer write is not a human
        // decision, and locking here would exclude the song from every future upgrade).
        song.Version = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Result.Ok(new ApplySongTagsResponse(song.SongId, produced.Count, request.TagModel));
    }
}
