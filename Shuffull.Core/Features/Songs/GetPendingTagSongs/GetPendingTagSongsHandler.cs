using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Nut.Results;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Persistence;
using Shuffull.Metadata.Tools;

namespace Shuffull.Core.Features.Songs.GetPendingTagSongs;

/// <summary>
/// Builds the producer's work list. Deliberately has NO dependency on the AI stack: the site's engine ships
/// disabled (all AI spend is the funnel's), so this must work purely from configuration and the ModelStrengths
/// ladder. It only decides WHICH songs are behind and WHAT tier they deserve — the funnel does the tagging.
/// </summary>
public class GetPendingTagSongsHandler(
    ShuffullContext context,
    ModelStrengths modelStrengths,
    IConfiguration configuration)
    : IRequestHandler<GetPendingTagSongsQuery, Result<PendingTagSongsResponse>>
{
    private const int DefaultLimit = 25;
    private const int MaxLimit = 200;

    /// <summary>
    /// The model a tier resolves to, read straight from configuration rather than through IAIServiceResolver
    /// (which is only registered when the site's own AI is enabled). Mirrors the resolver's fallback chain:
    /// the tier names a provider section, and a missing weak model falls back to that provider's strong one.
    /// </summary>
    internal string? ResolveTierModel(string tier)
    {
        var provider = configuration[$"AI:Tiers:{tier}"];
        if (string.IsNullOrWhiteSpace(provider))
        {
            provider = "OpenAI";
        }

        var strong = configuration[$"AI:{provider}:StrongModelName"] ?? configuration[$"AI:{provider}:ModelName"];
        return tier == "Weak"
            ? configuration[$"AI:{provider}:WeakModelName"] ?? strong
            : strong;
    }

    public async Task<Result<PendingTagSongsResponse>> Handle(GetPendingTagSongsQuery request, CancellationToken cancellationToken)
    {
        var limit = request.Limit <= 0 ? DefaultLimit : Math.Min(request.Limit, MaxLimit);

        var weakModel = ResolveTierModel("Weak");
        var strongModel = ResolveTierModel("Strong");

        // Fail SAFE, exactly as RetagStaleSongs does: an unregistered target model scores 0, which would make
        // every song look "behind" and hand the producer the entire library to re-tag. Returning nothing until
        // the map is fixed is the cheaper mistake.
        if (modelStrengths.GetStrength(weakModel) <= 0 && modelStrengths.GetStrength(strongModel) <= 0)
        {
            return Result.Ok(new PendingTagSongsResponse([], 0));
        }

        // Expressed as sets so the comparison happens in SQL — ModelStrengths is a config map the database
        // knows nothing about, so "at least as strong as X" has to be precomputed into a list of names.
        var weakEnough = modelStrengths.ModelsAtLeastAsStrongAs(weakModel).ToList();
        var strongEnough = modelStrengths.ModelsAtLeastAsStrongAs(strongModel).ToList();

        // Positive sentiment from ANY user earns the strong model. Love collapses into Like here; the site
        // already flattens it that way for YouTube parity, and the ladder only has two tiers.
        var likedSongIds = context.UserSongs
            .Where(us => us.LikeStatus == LikeStatus.Like || us.LikeStatus == LikeStatus.Love)
            .Select(us => us.SongId);

        var pending = context.Songs.Where(s =>
            !s.Exploratory                                    // un-vetted audition songs spend nothing until kept
            && (likedSongIds.Contains(s.SongId)
                    ? !strongEnough.Contains(s.TagModel ?? "")
                    : !weakEnough.Contains(s.TagModel ?? ""))
            // A curator lock still yields to a genuine MODEL upgrade, but only when there is a model to upgrade
            // FROM. UpdateSong does not stamp TagModel, so a locked song with a null one was tagged by a person
            // — overwriting that with the weak model would be a downgrade wearing an upgrade's clothes.
            && (!s.MetadataLocked || s.TagModel != null));

        var remaining = await pending.CountAsync(cancellationToken);

        // Oldest first: the queue drains in the order work was requested, so a long legacy backfill cannot be
        // starved by newer arrivals.
        var rows = await pending
            .OrderBy(s => s.Version)
            .Take(limit)
            .Select(s => new
            {
                s.SongId,
                s.Name,
                Artists = s.SongArtists.Select(sa => sa.Artist.Name).ToList(),
                IsLiked = likedSongIds.Contains(s.SongId),
                s.TagModel,
                s.PlainLyrics,
                s.SyncedLyrics,
                s.LyricsInstrumental,
                s.MeasuredBpm,
                s.CrestFactorDb,
                s.LoudnessRangeLu,
                s.OnsetsPerSecond,
                s.OriginalReleaseYear,
            })
            .ToListAsync(cancellationToken);

        var songs = rows
            .Select(r => new PendingTagSong(
                r.SongId, r.Name, r.Artists,
                r.IsLiked ? TagTiers.Strong : TagTiers.Weak,
                r.TagModel, r.PlainLyrics, r.SyncedLyrics, r.LyricsInstrumental,
                r.MeasuredBpm, r.CrestFactorDb, r.LoudnessRangeLu, r.OnsetsPerSecond, r.OriginalReleaseYear))
            .ToList();

        return Result.Ok(new PendingTagSongsResponse(songs, remaining));
    }
}
