using Nut.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shuffull.Api.Services;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Services;

/// <summary>
/// Persistence side of the keep-durability fix: promoting an audition song must clear <c>Exploratory</c> and
/// bump <c>Version</c> against a real database, without touching <c>TagModel</c> (which is what keeps the song
/// in the RetagStaleSongs work queue) and without any AI in the picture.
/// </summary>
public class AuditionPromotionServiceTest : IDisposable
{
    private readonly DatabaseFixture _database;
    private readonly AuditionPromotionService _service;

    public AuditionPromotionServiceTest()
    {
        _database = new DatabaseFixture();

        // The service opens its own scope per call and resolves a context from it, exactly as the request
        // pipeline does. Handing out a FRESH context per scope means the assertions below read rows that were
        // really committed to the shared in-memory database, not another context's tracked entities.
        var services = new ServiceCollection();
        services.AddScoped(_ => _database.CreateContext());
        _service = new AuditionPromotionService(services.BuildServiceProvider());
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<Song> SeedSongAsync(bool exploratory, string? tagModel = null, bool metadataLocked = false, DateTime? version = null)
    {
        var song = new Song
        {
            SongId = Guid.NewGuid().ToString(),
            Name = "Song",
            FileExtension = ".mp3",
            FileHash = Guid.NewGuid().ToString(),
            Exploratory = exploratory,
            TagModel = tagModel,
            MetadataLocked = metadataLocked,
            Version = version ?? new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        await _database.Context.Songs.AddAsync(song);
        await _database.Context.SaveChangesAsync();
        return song;
    }

    private async Task<Song> ReloadAsync(string songId)
    {
        _database.Context.ChangeTracker.Clear();
        return await _database.Context.Songs.AsNoTracking().SingleAsync(s => s.SongId == songId);
    }

    [Fact]
    public async Task ClearsExploratory_AndBumpsVersion()
    {
        var song = await SeedSongAsync(exploratory: true);
        var before = song.Version;

        var result = await _service.PromoteAsync([song.SongId]);

        Assert.True(result.IsOk);
        Assert.Equal(1, result.Get());

        var reloaded = await ReloadAsync(song.SongId);
        Assert.False(reloaded.Exploratory);
        Assert.True(reloaded.Version > before);
    }

    [Fact]
    public async Task LeavesTagModelNull_SoTheStaleSweepStillPicksItUp()
    {
        // The promoted song must remain matchable by RetagStaleSongs' work query:
        //   !MetadataLocked && !Exploratory && (TagModel == null || weaker-than-strong)
        var song = await SeedSongAsync(exploratory: true, tagModel: null);

        await _service.PromoteAsync([song.SongId]);

        var reloaded = await ReloadAsync(song.SongId);
        Assert.Null(reloaded.TagModel);
        Assert.False(reloaded.Exploratory);
        Assert.False(reloaded.MetadataLocked);
    }

    [Fact]
    public async Task DoesNotTouchAlreadyPromotedSongs_SoVersionDoesNotChurn()
    {
        var song = await SeedSongAsync(exploratory: false, tagModel: "gpt-5.5");
        var before = song.Version;

        var result = await _service.PromoteAsync([song.SongId]);

        Assert.True(result.IsOk);
        Assert.Equal(0, result.Get());

        var reloaded = await ReloadAsync(song.SongId);
        Assert.Equal(before, reloaded.Version);
        Assert.Equal("gpt-5.5", reloaded.TagModel);
    }

    [Fact]
    public async Task IsIdempotent()
    {
        var song = await SeedSongAsync(exploratory: true);

        var first = await _service.PromoteAsync([song.SongId]);
        var versionAfterFirst = (await ReloadAsync(song.SongId)).Version;
        var second = await _service.PromoteAsync([song.SongId]);

        Assert.Equal(1, first.Get());
        Assert.Equal(0, second.Get());
        Assert.Equal(versionAfterFirst, (await ReloadAsync(song.SongId)).Version);
    }

    [Fact]
    public async Task PromotesMetadataLockedSongs()
    {
        // The lock protects TAGS from being rewritten; it does not mean "leave this in the audition pool".
        // Leaving it exploratory would expose it to DeletePlaylistHandler's purge.
        var song = await SeedSongAsync(exploratory: true, metadataLocked: true);

        var result = await _service.PromoteAsync([song.SongId]);

        Assert.Equal(1, result.Get());
        var reloaded = await ReloadAsync(song.SongId);
        Assert.False(reloaded.Exploratory);
        Assert.True(reloaded.MetadataLocked); // the lock itself is untouched
    }

    [Fact]
    public async Task PromotesTheWholeBatch_AndIgnoresUnknownIds()
    {
        var a = await SeedSongAsync(exploratory: true);
        var b = await SeedSongAsync(exploratory: true);
        var alreadyPromoted = await SeedSongAsync(exploratory: false);

        var result = await _service.PromoteAsync([a.SongId, b.SongId, alreadyPromoted.SongId, "does-not-exist"]);

        Assert.Equal(2, result.Get());
        Assert.False((await ReloadAsync(a.SongId)).Exploratory);
        Assert.False((await ReloadAsync(b.SongId)).Exploratory);
    }

    [Fact]
    public async Task SharesOneVersionAcrossTheBatch()
    {
        var a = await SeedSongAsync(exploratory: true);
        var b = await SeedSongAsync(exploratory: true);

        await _service.PromoteAsync([a.SongId, b.SongId]);

        Assert.Equal((await ReloadAsync(a.SongId)).Version, (await ReloadAsync(b.SongId)).Version);
    }

    [Theory]
    [InlineData()]
    public async Task EmptyInput_IsANoOp(params string[] ids)
    {
        var result = await _service.PromoteAsync(ids);
        Assert.True(result.IsOk);
        Assert.Equal(0, result.Get());
    }

    [Fact]
    public async Task BlankAndDuplicateIds_AreCollapsed()
    {
        var song = await SeedSongAsync(exploratory: true);

        var result = await _service.PromoteAsync([song.SongId, song.SongId, "", "   "]);

        Assert.Equal(1, result.Get()); // counted once, not twice
        Assert.False((await ReloadAsync(song.SongId)).Exploratory);
    }
}
