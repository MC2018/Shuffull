using Microsoft.Extensions.Configuration;
using Nut.Results;
using Shuffull.Core.Features.Songs.GetPendingTagSongs;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Tests.Infrastructure;
using Shuffull.Core.Tools;
using Shuffull.Metadata.Services.AI;
using Shuffull.Metadata.Tools;
using Shuffull.Shared.Tools;

namespace Shuffull.Core.Tests.Features.Songs;

/// <summary>
/// The producer's work queue. Covers the derived tier (positive sentiment earns the strong model), oldest-first
/// ordering, the curator-lock override rule, and the fail-safe when the target model isn't registered.
/// </summary>
public class GetPendingTagSongsHandlerTest : IDisposable
{
    private readonly DatabaseFixture _database;

    public GetPendingTagSongsHandlerTest() => _database = new DatabaseFixture();

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private static ModelStrengths Strengths() => new(new Dictionary<string, int>
    {
        ["weak-model"] = 10,
        ["strong-model"] = 30,
        ["frontier-model"] = 50, // registered, but stronger than either tier
    });

    /// <summary>Weak tier served by "Meta", strong by "OpenAI" — the real prod shape.</summary>
    private static IConfiguration Config(string? weak = "weak-model", string? strong = "strong-model") =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AI:Tiers:Weak"] = "Meta",
            ["AI:Tiers:Strong"] = "OpenAI",
            ["AI:Meta:WeakModelName"] = weak,
            ["AI:OpenAI:StrongModelName"] = strong,
        }).Build();

    private async Task<Song> SeedAsync(string? tagModel = null, bool locked = false, bool exploratory = false,
                                       DateTime? version = null, LikeStatus? like = null, string? externalId = "yt-abc",
                                       int tagRevision = TagRevision.Current)
    {
        var song = new Song
        {
            SongId = IdGenerator.Generate(),
            Name = $"song-{IdGenerator.Generate()[..6]}",
            FileExtension = ".mp3",
            FileHash = IdGenerator.Generate(),
            ExternalSongId = externalId,
            TagModel = tagModel,
            TagRevision = tagRevision,
            MetadataLocked = locked,
            Exploratory = exploratory,
            Version = version ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        _database.Context.Songs.Add(song);
        await _database.Context.SaveChangesAsync();

        if (like is LikeStatus status)
        {
            var user = new User { UserId = IdGenerator.Generate(), Username = $"u-{IdGenerator.Generate()[..6]}", ServerHash = "h", Version = DateTime.UtcNow };
            _database.Context.Users.Add(user);
            await _database.Context.SaveChangesAsync();
            _database.Context.UserSongs.Add(new UserSong
            {
                UserId = user.UserId, SongId = song.SongId, LikeStatus = status,
                Version = DateTime.UtcNow, LastPlayed = DateTime.UtcNow,
            });
            await _database.Context.SaveChangesAsync();
        }

        return song;
    }

    private Task<Result<PendingTagSongsResponse>> RunAsync(int limit = 25, IConfiguration? config = null)
        => new GetPendingTagSongsHandler(_database.Context, Strengths(), config ?? Config())
            .Handle(new GetPendingTagSongsQuery(limit), CancellationToken.None);

    [Theory]
    [InlineData(null, TagTiers.Weak)]              // never rated -> cheap tier
    [InlineData(LikeStatus.Neutral, TagTiers.Weak)] // liked then UN-liked -> back to weak, no residue
    [InlineData(LikeStatus.Dislike, TagTiers.Weak)]
    [InlineData(LikeStatus.Like, TagTiers.Strong)]
    [InlineData(LikeStatus.Love, TagTiers.Strong)]  // Love collapses into the strong tier
    public async Task DerivesTierFromCurrentSentiment(LikeStatus? like, string expectedTier)
    {
        await SeedAsync(like: like);

        var song = Assert.Single((await RunAsync()).Get().Songs);

        Assert.Equal(expectedTier, song.Tier);
    }

    [Fact]
    public async Task OlderRevision_IsOfferedForOneRescore()
    {
        // The weak model already tagged it, so only the prompt revision makes it due (shuffull-workspace#41).
        var behind = await SeedAsync(tagModel: "weak-model", tagRevision: TagRevision.Current - 1);
        await SeedAsync(tagModel: "weak-model");                                // current revision: done

        var songs = (await RunAsync()).Get().Songs;

        var song = Assert.Single(songs);
        Assert.Equal(behind.SongId, song.SongId);
        Assert.Equal(TagTiers.Weak, song.Tier);
    }

    [Fact]
    public async Task OlderRevision_TaggedAboveTheWeakTier_IsRescoredOnTheStrongTier()
    {
        // Unliked songs normally get the weak model, but the weak model's write would be refused as a provenance
        // downgrade (TagUpgradeRule.WouldRescore). It has to be the strong tier, or the song loops forever.
        await SeedAsync(tagModel: "strong-model", tagRevision: TagRevision.Current - 1);

        var song = Assert.Single((await RunAsync()).Get().Songs);

        Assert.Equal(TagTiers.Strong, song.Tier);
    }

    [Fact]
    public async Task OlderRevision_TaggedAboveBothTiers_IsNotOffered()
    {
        // No tier could write it (the write needs a model at least as strong), so offering it would refuse it
        // forever. The same holds for everything above the weak tier while the strong model is unregistered.
        await SeedAsync(tagModel: "frontier-model", tagRevision: TagRevision.Current - 1);
        var strongTagged = await SeedAsync(tagModel: "strong-model", tagRevision: TagRevision.Current - 1);

        Assert.Equal([strongTagged.SongId], (await RunAsync()).Get().Songs.Select(s => s.SongId));
        Assert.Empty((await RunAsync(config: Config(strong: null))).Get().Songs.Where(s => s.SongId == strongTagged.SongId));
    }

    [Fact]
    public async Task RealUpgrades_ComeBeforeRevisionRescores()
    {
        // A library-wide re-score must not queue ahead of a Keep or a Like, even an older one behind it.
        var oldRescore = await SeedAsync(tagModel: "weak-model", tagRevision: TagRevision.Current - 1,
                                         version: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var newKeep = await SeedAsync(version: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)); // untagged

        var songs = (await RunAsync()).Get().Songs;

        Assert.Equal([newKeep.SongId, oldRescore.SongId], songs.Select(s => s.SongId));
    }

    [Fact]
    public async Task OlderRevision_CuratorLockedSong_IsNotRescored()
    {
        // UpdateSong sets tags and Energy by hand but keeps the old TagModel. A re-score would overwrite them.
        await SeedAsync(tagModel: "weak-model", locked: true, tagRevision: TagRevision.Current - 1);

        Assert.Empty((await RunAsync()).Get().Songs);
    }

    [Fact]
    public async Task OlderRevision_IsNotRescored_WhileTheWeakModelIsUnregistered()
    {
        // Every tag model would look stronger than an unregistered weak model, sending the library to the
        // strong tier. Returning nothing until the config is fixed is the cheaper mistake.
        await SeedAsync(tagModel: "strong-model", tagRevision: TagRevision.Current - 1);

        Assert.Empty((await RunAsync(config: Config(weak: "not-in-the-map"))).Get().Songs);
    }

    [Fact]
    public async Task OlderRevision_ExploratorySongIsNotRescored()
    {
        await SeedAsync(tagModel: "weak-model", exploratory: true, tagRevision: TagRevision.Current - 1);

        Assert.Empty((await RunAsync()).Get().Songs);
    }

    [Fact]
    public async Task ExploratorySongsAreNeverOffered()
    {
        // Un-vetted audition songs must cost nothing until the user keeps one.
        await SeedAsync(exploratory: true);

        Assert.Empty((await RunAsync()).Get().Songs);
    }

    [Fact]
    public async Task ExcludesSongsAlreadyAtOrAboveTheirTier()
    {
        await SeedAsync(tagModel: "weak-model");                       // weak tier, already weak -> done
        await SeedAsync(tagModel: "strong-model", like: LikeStatus.Like); // strong tier, already strong -> done
        var needsUpgrade = await SeedAsync(tagModel: "weak-model", like: LikeStatus.Like); // weak tags, strong tier

        var songs = (await RunAsync()).Get().Songs;

        Assert.Equal([needsUpgrade.SongId], songs.Select(s => s.SongId));
        Assert.Equal(TagTiers.Strong, songs[0].Tier);
    }

    [Fact]
    public async Task OrdersOldestFirst_AndRespectsTheLimit()
    {
        var oldest = await SeedAsync(version: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var middle = await SeedAsync(version: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        await SeedAsync(version: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));

        var response = (await RunAsync(limit: 2)).Get();

        Assert.Equal([oldest.SongId, middle.SongId], response.Songs.Select(s => s.SongId));
        Assert.Equal(3, response.Remaining); // the full outstanding count, not just this page
    }

    [Fact]
    public async Task ASongThatIsNeverTagged_NoLongerBlocksTheNextPage()
    {
        // Shuffull#40: only a successful write moved a song, so a page of songs the producer can't tag (nothing
        // inferable, held in a batch, a write refused) was handed back first on every cycle, and nothing behind
        // it was ever reached.
        var stuck1 = await SeedAsync(version: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var stuck2 = await SeedAsync(version: new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        var behind = await SeedAsync(version: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));

        var first = (await RunAsync(limit: 2)).Get();
        Assert.Equal([stuck1.SongId, stuck2.SongId], first.Songs.Select(s => s.SongId));

        // The producer writes nothing back for either. The next page reaches the song behind them.
        var second = (await RunAsync(limit: 2)).Get();
        Assert.Equal([behind.SongId], second.Songs.Select(s => s.SongId));
        Assert.Equal(3, second.Remaining); // still outstanding, just backing off
    }

    [Fact]
    public async Task ASongBackingOff_IsOfferedAgainOnceItsDelayPasses_AheadOfNewerWork()
    {
        // A song held for an AI batch has to come back, and in its old place: oldest first.
        var held = await SeedAsync(version: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var newer = await SeedAsync(version: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        await RunAsync(limit: 1);

        await using (var context = _database.CreateContext())
        {
            var song = await context.Songs.FindAsync(held.SongId);
            song!.NextTagAttemptAt = DateTime.UtcNow.AddSeconds(-1);
            await context.SaveChangesAsync();
        }

        var page = (await RunAsync(limit: 2)).Get();
        Assert.Equal([held.SongId, newer.SongId], page.Songs.Select(s => s.SongId));
    }

    [Fact]
    public async Task HandingOutCountsAnAttempt_WithoutBumpingVersion()
    {
        // Version drives client sync. Queue bookkeeping must not make every client re-fetch the song.
        var version = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var song = await SeedAsync(version: version);

        var before = DateTime.UtcNow;
        await RunAsync();

        await using var check = _database.CreateContext();
        var reloaded = await check.Songs.FindAsync(song.SongId);
        Assert.Equal(1, reloaded!.TagAttempts);
        Assert.InRange(reloaded.NextTagAttemptAt!.Value, before.AddMinutes(5), DateTime.UtcNow.AddMinutes(5));
        Assert.Equal(version, reloaded.Version);
    }

    [Theory]
    [InlineData(1, 5)]      // sooner than the funnel's 10-minute cycle, so a batch-held song is back next pass
    [InlineData(2, 10)]
    [InlineData(4, 40)]
    [InlineData(5, 60)]     // capped at an hour: a song held through chained batches never waits much longer
    [InlineData(1000, 60)]
    public void RetryDelayDoublesUpToAnHour(int attempts, int minutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(minutes), GetPendingTagSongsHandler.RetryDelay(attempts));
    }

    [Theory]
    [InlineData(null, false, "weak-model")]
    [InlineData(null, true, "weak-model")]
    [InlineData("weak-model", false, "weak-model")]
    [InlineData("weak-model", true, "weak-model")]
    [InlineData("weak-model", false, "strong-model")]
    [InlineData("weak-model", true, "strong-model")]
    [InlineData("strong-model", false, "weak-model")]
    [InlineData("strong-model", true, "strong-model")]
    [InlineData("retired-model", false, "weak-model")]
    [InlineData("retired-model", true, "strong-model")]
    [InlineData("WEAK-MODEL", false, "weak-model")] // ModelStrengths ignores case, so the SQL form must too
    public async Task OffersExactlyTheSongsApplySongTagsWouldAccept(string? tagModel, bool locked, string target)
    {
        // The queue and the write share TagUpgradeRule, in SQL and in memory. If they disagree, the producer is
        // handed songs it can never write (or never offered songs it could), which is how #40 churned.
        var song = await SeedAsync(tagModel: tagModel, locked: locked);

        var offered = (await RunAsync(config: Config(weak: target))).Get().Songs.Any(s => s.SongId == song.SongId);

        Assert.Equal(TagUpgradeRule.WouldUpgrade(song, target, Strengths()), offered);
    }

    [Fact]
    public async Task LockedSongWithNoTagModel_IsLeftAlone()
    {
        // UpdateSong doesn't stamp TagModel, so a locked song with a null one was tagged by a PERSON. There is
        // no model to upgrade from, and letting the weak model overwrite it would be a downgrade.
        await SeedAsync(tagModel: null, locked: true);

        Assert.Empty((await RunAsync()).Get().Songs);
    }

    [Fact]
    public async Task LockedSongWithWeakerTagModel_IsOfferedAsAnUpgrade()
    {
        var locked = await SeedAsync(tagModel: "weak-model", locked: true, like: LikeStatus.Like);

        var song = Assert.Single((await RunAsync()).Get().Songs);

        Assert.Equal(locked.SongId, song.SongId);
        Assert.Equal(TagTiers.Strong, song.Tier);
    }

    [Fact]
    public async Task UnregisteredTargetModel_OffersNothing()
    {
        // Fail-safe: an unregistered model scores 0, which would make the whole library look behind.
        await SeedAsync();

        var response = (await RunAsync(config: Config(weak: "not-in-the-map", strong: "also-missing"))).Get();

        Assert.Empty(response.Songs);
        Assert.Equal(0, response.Remaining);
    }

    [Fact]
    public async Task EmptyStrongModel_FallsBackToLegacyModelName_SoLikesStillQueueTheUpgrade()
    {
        // Prod on 2026-10-03: compose rendered AI__OpenAI__StrongModelName= (empty). `??` stopped at the "", the
        // strong tier scored 0, every registered model counted as "strong enough", and a liked song on weaker
        // tags was never queued. Blank must mean missing.
        var liked = await SeedAsync(tagModel: "weak-model", like: LikeStatus.Like);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AI:Tiers:Weak"] = "Meta",
            ["AI:Tiers:Strong"] = "OpenAI",
            ["AI:Meta:WeakModelName"] = "weak-model",
            ["AI:OpenAI:StrongModelName"] = "",
            ["AI:OpenAI:ModelName"] = "strong-model",
        }).Build();

        var song = Assert.Single((await RunAsync(config: config)).Get().Songs);

        Assert.Equal(liked.SongId, song.SongId);
        Assert.Equal(TagTiers.Strong, song.Tier);
    }

    [Fact]
    public async Task EmptyWeakModel_FallsBackToTheStrongModel()
    {
        // Same blank-is-missing rule on the weak side: an empty WeakModelName means "use the strong model".
        var unliked = await SeedAsync(tagModel: "weak-model");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AI:OpenAI:WeakModelName"] = "",
            ["AI:OpenAI:StrongModelName"] = "strong-model",
        }).Build();

        var song = Assert.Single((await RunAsync(config: config)).Get().Songs);

        Assert.Equal(unliked.SongId, song.SongId);
        Assert.Equal(TagTiers.Weak, song.Tier);
    }

    [Fact]
    public async Task CarriesExternalSongId_SoTheProducerCanReuseItsCache()
    {
        // The funnel keys its AI-response cache on the video id it saw at ingest. Handing the same id back
        // lets an already-tagged song be re-tagged from cache instead of paying the engine again.
        await SeedAsync(externalId: "dQw4w9WgXcQ");

        Assert.Equal("dQw4w9WgXcQ", Assert.Single((await RunAsync()).Get().Songs).ExternalSongId);
    }

    [Fact]
    public async Task ManualUploadsHaveNoExternalId()
    {
        await SeedAsync(externalId: null);

        Assert.Null(Assert.Single((await RunAsync()).Get().Songs).ExternalSongId);
    }

    [Fact]
    public async Task CarriesTheEngineInputs()
    {
        var song = await SeedAsync();
        song.MeasuredBpm = 128;
        song.OriginalReleaseYear = 2019;
        song.PlainLyrics = "la la";
        await _database.Context.SaveChangesAsync();

        var result = Assert.Single((await RunAsync()).Get().Songs);

        Assert.Equal(128, result.MeasuredBpm);
        Assert.Equal(2019, result.OriginalReleaseYear);
        Assert.Equal("la la", result.PlainLyrics);
    }
}
