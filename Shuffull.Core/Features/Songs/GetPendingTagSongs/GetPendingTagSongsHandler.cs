using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Nut.Results;
using Shuffull.Core.Alerts;
using Shuffull.Core.Errors;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Persistence;
using Shuffull.Core.Services.Alerts;
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
    IConfiguration configuration,
    IHealthSignals signals,
    TimeProvider timeProvider)
    : IRequestHandler<GetPendingTagSongsQuery, Result<PendingTagSongsResponse>>
{
    private const int DefaultLimit = 25;
    /// <summary>The largest page the queue hands out.</summary>
    public const int MaxLimit = 200;

    /// <summary>
    /// The first retry comes sooner than the funnel's 10-minute cycle, so a song the funnel reports as held for an AI
    /// batch is offered again on its next cycle (HoldTagSongs puts it back here). Each unreported miss doubles the
    /// wait, up to an hour, so a song that can never be tagged drops from every cycle to once an hour. A funnel that
    /// predates hold reports (shuffull-workspace#36) gets its held songs on that same ladder.
    /// </summary>
    public static readonly TimeSpan FirstRetryDelay = TimeSpan.FromMinutes(5);
    /// <summary>The longest a song sits out between hand-outs, and how far back in line a held song rejoins.</summary>
    public static readonly TimeSpan MaxRetryDelay = TimeSpan.FromHours(1);

    public async Task<Result<PendingTagSongsResponse>> Handle(GetPendingTagSongsQuery request, CancellationToken cancellationToken)
    {
        var limit = request.Limit <= 0 ? DefaultLimit : Math.Min(request.Limit, MaxLimit);

        // A declared model must be on the ladder. Scoring an unknown one 0 would either make every song look
        // behind (the whole library handed out, every write then refused) or, through the fail-safe below, quietly
        // offer nothing while the funnel looks healthy. Neither is safe to guess between, and the two maps are
        // meant to be identical, so an unknown model is a config fault: refuse the poll loudly, and alert.
        var declaredWeak = NonBlank(request.WeakModel);
        var declaredStrong = NonBlank(request.StrongModel);
        var unknown = new[] { declaredWeak, declaredStrong }
            .Where(m => m is not null && !modelStrengths.Knows(m))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (unknown.Count > 0)
        {
            var message = $"The producer tags with {string.Join(" and ", unknown.Select(m => $"'{m}'"))}, which "
                + "AI:ModelStrengths doesn't list, so no work is handed out. Register it in both ModelStrengths maps.";
            await signals.FailureAsync(ShuffullSignalRules.PendingTagsModels, message, cancellationToken);
            return Result.Error<PendingTagSongsResponse>(new KindedError(ErrorKind.Invalid, message));
        }
        // Declared or not: a funnel rolled back to a build that declares nothing must still clear the alert.
        await signals.SuccessAsync(ShuffullSignalRules.PendingTagsModels, cancellationToken);

        // A tier the producer didn't declare is read straight from configuration rather than through
        // IAIServiceResolver, which is only registered when the site's own AI is enabled. Blank counts as missing —
        // see TierModelConfiguration.
        var weakModel = declaredWeak ?? TierModelConfiguration.ResolveModel(configuration, TierModelConfiguration.Weak);
        var strongModel = declaredStrong ?? TierModelConfiguration.ResolveModel(configuration, TierModelConfiguration.Strong);

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
        // the write is refused (TagUpgradeRule.WouldRescore). So a song whose tags came from a model STRONGER than
        // the weak tier is re-scored on the strong tier, liked or not, and a song tagged above BOTH tiers is not
        // offered at all: no tier could write it, and it would come back refused forever (Shuffull#40). That
        // includes every song above the weak tier while the strong model is unregistered. Off entirely while the
        // weak model is unregistered: every tag model would look stronger than it.
        var weakStrength = modelStrengths.GetStrength(weakModel);
        var strongStrength = modelStrengths.GetStrength(strongModel);
        var rescoreEnabled = weakStrength > 0;
        var strongerThanWeak = weakEnough
            .Where(m => modelStrengths.GetStrength(m) > weakStrength)
            .Select(m => m.ToLowerInvariant())
            .ToList();
        // ModelsAtLeastAsStrongAs(null) is every registered model; an unregistered TagModel is never rescorable.
        var rescorable = modelStrengths.ModelsAtLeastAsStrongAs(null)
            .Where(m => modelStrengths.GetStrength(m) <= Math.Max(weakStrength, strongStrength))
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
        var upgradeIds = candidates
            .Where(s => likedSongIds.Contains(s.SongId))
            .Where(TagUpgradeRule.WouldUpgrade(strongEnough))
            .Select(s => s.SongId)
            .Concat(candidates
                .Where(s => !likedSongIds.Contains(s.SongId))
                .Where(TagUpgradeRule.WouldUpgrade(weakEnough))
                .Select(s => s.SongId));
        var rescoreIds = candidates
            .Where(_ => rescoreEnabled)
            .Where(TagUpgradeRule.IsBehindRevision(TagRevision.Current))
            .Where(s => rescorable.Contains(s.TagModel!.ToLower()))
            .Select(s => s.SongId);
        var pending = context.Songs.Where(s => upgradeIds.Contains(s.SongId) || rescoreIds.Contains(s.SongId));

        // The whole outstanding backlog, including songs still backing off.
        var remaining = await pending.CountAsync(cancellationToken);

        // Real upgrades (a Keep, a Like, untagged songs) first, then revision re-scores, so a library-wide re-score
        // never queues ahead of what a user just asked for. Oldest first within each: the queue drains in the order
        // work was requested, so a long legacy backfill cannot be starved by newer arrivals. A song that was handed
        // out and is still behind (no tags inferable, held in a batch, a write refused) sits out its backoff instead
        // of filling the page.
        //
        // A song the producer reported held comes back every cycle until its batch lands, and is usually among the
        // oldest. Kept at its old place, 25 songs held at once (a strong-model change makes every liked song behind)
        // would fill every page until their batches land, and a fresh Keep would wait behind them. Sent to the very
        // end, enough newer work would starve it instead. So each hold sends it to the back of the line as of
        // MaxRetryDelay after it is due again (Song.TagQueuePlace): work asked for within that hour goes first, and
        // anything newer waits behind it. Neither side can hold the other off for good. A producer that doesn't
        // report holds has no held songs, and sees the order it always did.
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var rows = await pending
            .Where(s => s.NextTagAttemptAt == null || s.NextTagAttemptAt <= now)
            .OrderBy(s => upgradeIds.Contains(s.SongId) ? 0 : 1)
            .ThenBy(Song.TagQueuePlace(MaxRetryDelay))
            .ThenBy(s => s.Version) // songs held in one report share a place
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
                s.FileHash,
                s.FileExtension,
                s.TagAttempts,
            })
            .ToListAsync(cancellationToken);

        // Count the hand-out as an attempt. The site can't see why the producer fails a song, or whether it
        // reports back at all, so the only safe signal is that it was offered. Writing tags resets the count
        // (Song.ResetTagAttempts), and so does a reported hold (HoldTagSongs), which is not a failure. Version is
        // untouched: this is queue state, not a change clients need.
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
                r.MeasuredBpm, r.CrestFactorDb, r.LoudnessRangeLu, r.OnsetsPerSecond, r.OriginalReleaseYear,
                r.FileHash, r.FileExtension))
            .ToList();

        return Result.Ok(new PendingTagSongsResponse(songs, remaining));
    }

    /// <summary>How long a song sits out after its <paramref name="attempts"/>th hand-out.</summary>
    public static TimeSpan RetryDelay(int attempts)
    {
        var delay = FirstRetryDelay * Math.Pow(2, Math.Clamp(attempts - 1, 0, 30));
        return delay < MaxRetryDelay ? delay : MaxRetryDelay;
    }

    private static string? NonBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
