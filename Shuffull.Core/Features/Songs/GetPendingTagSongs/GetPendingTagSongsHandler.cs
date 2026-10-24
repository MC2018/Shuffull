using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Nut.Results;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Persistence;
using Shuffull.Core.Tools;
using Shuffull.Metadata.Services.AI;
using Shuffull.Metadata.Tools;

namespace Shuffull.Core.Features.Songs.GetPendingTagSongs;

/// <summary>
/// Builds the producer's work list. Deliberately has NO dependency on the AI stack: the site's engine ships
/// disabled (all AI spend is the funnel's), so this must work purely from configuration and the ModelStrengths
/// ladder. It only decides WHICH songs are behind and WHAT tier they deserve — the funnel does the tagging.
///
/// <para>Not read-only: each song handed out counts an attempt and is skipped until its backoff passes, which is
/// what keeps a song that can't be tagged from holding the head of the queue (Shuffull#40). Calling the endpoint
/// by hand backs those songs off too.</para>
/// </summary>
public class GetPendingTagSongsHandler(
    ShuffullContext context,
    ModelStrengths modelStrengths,
    IConfiguration configuration)
    : IRequestHandler<GetPendingTagSongsQuery, Result<PendingTagSongsResponse>>
{
    private const int DefaultLimit = 25;
    private const int MaxLimit = 200;

    // The first retry comes sooner than the funnel's 10-minute cycle. Each further miss doubles the wait, up to an
    // hour. The cap is low because a hold is not a failure: the funnel tags a song in up to three chained AI
    // batches, holding it each time and needing it offered again to queue the next, so a held song should wait at
    // most an hour past its batch landing. A song that can never be tagged still drops from every cycle to once
    // an hour. The site can't tell the two apart; the funnel would have to report holds (shuffull-workspace#36).
    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromHours(1);

    public async Task<Result<PendingTagSongsResponse>> Handle(GetPendingTagSongsQuery request, CancellationToken cancellationToken)
    {
        var limit = request.Limit <= 0 ? DefaultLimit : Math.Min(request.Limit, MaxLimit);

        // Read straight from configuration rather than through IAIServiceResolver, which is only registered when
        // the site's own AI is enabled. Blank counts as missing — see TierModelConfiguration.
        var weakModel = TierModelConfiguration.ResolveModel(configuration, TierModelConfiguration.Weak);
        var strongModel = TierModelConfiguration.ResolveModel(configuration, TierModelConfiguration.Strong);

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

        // A re-score for a newer prompt revision must run a model no weaker than the one that tagged the song, or
        // the write is refused (TagUpgradeRule.WouldRescore) and the song's provenance would drop. So a song whose
        // tags came from a model STRONGER than the weak tier is re-scored on the strong tier, liked or not. Off
        // entirely while the weak model is unregistered: every tag model would then look stronger than it, and
        // the whole library would go to the strong tier.
        var weakStrength = modelStrengths.GetStrength(weakModel);
        var rescoreEnabled = weakStrength > 0;
        var strongerThanWeak = weakEnough
            .Where(m => modelStrengths.GetStrength(m) > weakStrength)
            .Select(m => m.ToLowerInvariant())
            .ToList();

        // Positive sentiment from ANY user earns the strong model. Love collapses into Like here; the site
        // already flattens it that way for YouTube parity, and the ladder only has two tiers.
        var likedSongIds = context.UserSongs
            .Where(us => us.LikeStatus == LikeStatus.Like || us.LikeStatus == LikeStatus.Love)
            .Select(us => us.SongId);

        // A song is pending when its tier's target model would be an upgrade on it. TagUpgradeRule is the same
        // rule ApplySongTags refuses by, so anything handed out here is a write the site will accept. Un-vetted
        // audition songs spend nothing until kept.
        // Matched by id rather than used directly, because EF can't project the artists collection off a UNION.
        // A song tagged under an older prompt revision is pending too, once (shuffull-workspace#41).
        var candidates = context.Songs.Where(s => !s.Exploratory);
        var pendingIds = candidates
            .Where(s => likedSongIds.Contains(s.SongId))
            .Where(TagUpgradeRule.WouldUpgrade(strongEnough))
            .Select(s => s.SongId)
            .Concat(candidates
                .Where(s => !likedSongIds.Contains(s.SongId))
                .Where(TagUpgradeRule.WouldUpgrade(weakEnough))
                .Select(s => s.SongId))
            .Concat(candidates
                .Where(_ => rescoreEnabled)
                .Where(TagUpgradeRule.IsBehindRevision(TagRevision.Current))
                .Select(s => s.SongId));
        var pending = context.Songs.Where(s => pendingIds.Contains(s.SongId));

        // The whole outstanding backlog, including songs still backing off.
        var remaining = await pending.CountAsync(cancellationToken);

        // Oldest first among the songs that are due: the queue drains in the order work was requested, so a long
        // legacy backfill cannot be starved by newer arrivals. A song that was handed out and is still behind (no
        // tags inferable, held in a batch, a write refused) sits out its backoff instead of filling the page.
        var now = DateTime.UtcNow;
        var rows = await pending
            .Where(s => s.NextTagAttemptAt == null || s.NextTagAttemptAt <= now)
            .OrderBy(s => s.Version)
            .Take(limit)
            .Select(s => new
            {
                s.SongId,
                s.ExternalSongId,
                s.Name,
                Artists = s.SongArtists.Select(sa => sa.Artist.Name).ToList(),
                IsLiked = likedSongIds.Contains(s.SongId),
                TaggedAboveWeak = s.TagModel != null && strongerThanWeak.Contains(s.TagModel.ToLower()),
                s.TagModel,
                s.PlainLyrics,
                s.SyncedLyrics,
                s.LyricsInstrumental,
                s.MeasuredBpm,
                s.CrestFactorDb,
                s.LoudnessRangeLu,
                s.OnsetsPerSecond,
                s.OriginalReleaseYear,
                s.TagAttempts,
            })
            .ToListAsync(cancellationToken);

        // Count the hand-out as an attempt. The site can't see why the producer fails a song, or whether it
        // reports back at all, so the only safe signal is that it was offered. Writing tags resets the count
        // (Song.ResetTagAttempts). Version is untouched: this is queue state, not a change clients need.
        foreach (var batch in rows.GroupBy(r => r.TagAttempts + 1))
        {
            var ids = batch.Select(r => r.SongId).ToList();
            var attempts = batch.Key;
            var nextAttemptAt = now + RetryDelay(attempts);
            await context.Songs
                .Where(s => ids.Contains(s.SongId))
                .ExecuteUpdateAsync(set => set
                    .SetProperty(s => s.TagAttempts, attempts)
                    .SetProperty(s => s.NextTagAttemptAt, nextAttemptAt), cancellationToken);
        }

        var songs = rows
            .Select(r => new PendingTagSong(
                r.SongId, r.ExternalSongId, r.Name, r.Artists,
                r.IsLiked || r.TaggedAboveWeak ? TagTiers.Strong : TagTiers.Weak,
                r.TagModel, r.PlainLyrics, r.SyncedLyrics, r.LyricsInstrumental,
                r.MeasuredBpm, r.CrestFactorDb, r.LoudnessRangeLu, r.OnsetsPerSecond, r.OriginalReleaseYear))
            .ToList();

        return Result.Ok(new PendingTagSongsResponse(songs, remaining));
    }

    /// <summary>How long a song sits out after its <paramref name="attempts"/>th hand-out.</summary>
    public static TimeSpan RetryDelay(int attempts)
    {
        var delay = FirstRetryDelay * Math.Pow(2, Math.Clamp(attempts - 1, 0, 30));
        return delay < MaxRetryDelay ? delay : MaxRetryDelay;
    }
}
