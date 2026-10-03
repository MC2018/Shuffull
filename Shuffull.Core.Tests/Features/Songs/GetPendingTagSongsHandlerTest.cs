using Microsoft.Extensions.Configuration;
using Nut.Results;
using Shuffull.Core.Features.Songs.GetPendingTagSongs;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Tests.Infrastructure;
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
                                       DateTime? version = null, LikeStatus? like = null, string? externalId = "yt-abc")
    {
        var song = new Song
        {
            SongId = IdGenerator.Generate(),
            Name = $"song-{IdGenerator.Generate()[..6]}",
            FileExtension = ".mp3",
            FileHash = IdGenerator.Generate(),
            ExternalSongId = externalId,
            TagModel = tagModel,
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
