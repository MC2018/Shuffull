using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nut.Results;
using Shuffull.Api.Services;
using Shuffull.Core.Features.Songs.KeepSongs;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Services;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Features.Songs.KeepSongs;

/// <summary>
/// The Keep endpoint's handler against a real database and the real <see cref="AuditionPromotionService"/>:
/// it promotes only songs in the caller's library, reports the rest per item, and never needs AI.
/// </summary>
public class KeepSongsHandlerTest : IDisposable
{
    private static readonly DateTime OldVersion = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly DatabaseFixture _database;
    private readonly AuditionPromotionService _promotion;

    public KeepSongsHandlerTest()
    {
        _database = new DatabaseFixture();

        // Promotion opens its own scope per call, as in the request pipeline; a fresh context per scope means the
        // assertions below read committed rows.
        var services = new ServiceCollection();
        services.AddScoped(_ => _database.CreateContext());
        _promotion = new AuditionPromotionService(services.BuildServiceProvider());
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Records what was promoted and can be scripted to fail (a down/locked database).</summary>
    private sealed class SpyPromotion(Result<int>? scripted = null) : IAuditionPromotionService
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];
        public Task<Result<int>> PromoteAsync(IReadOnlyList<string> songIds, CancellationToken cancellationToken = default!)
        {
            Calls.Add(songIds);
            return Task.FromResult(scripted ?? Result.Ok(songIds.Count));
        }
    }

    private Task<Result<KeepSongsResponse>> RunAsync(string userId, params string[] songIds)
        => new KeepSongsHandler(_database.UnitOfWork, _promotion)
            .Handle(new KeepSongsCommand(userId, songIds), CancellationToken.None);

    private async Task<User> SeedUserAsync()
    {
        var user = new User
        {
            UserId = Guid.NewGuid().ToString(),
            Username = $"user-{Guid.NewGuid():N}",
            Version = OldVersion,
            ServerHash = "server-hash",
        };
        await _database.Context.Users.AddAsync(user);
        await _database.Context.SaveChangesAsync();
        return user;
    }

    /// <summary>Seeds a song; <paramref name="owner"/> gets a UserSong for it, as an import does.</summary>
    private async Task<Song> SeedSongAsync(bool exploratory, User? owner = null)
    {
        var song = new Song
        {
            SongId = Guid.NewGuid().ToString(),
            Name = "Song",
            FileExtension = ".mp3",
            FileHash = Guid.NewGuid().ToString(),
            Exploratory = exploratory,
            Version = OldVersion,
        };
        await _database.Context.Songs.AddAsync(song);
        if (owner is not null)
        {
            await _database.Context.UserSongs.AddAsync(new UserSong
            {
                UserId = owner.UserId,
                SongId = song.SongId,
                LastPlayed = DateTime.MinValue,
                Version = OldVersion,
            });
        }
        await _database.Context.SaveChangesAsync();
        return song;
    }

    private async Task<Song> ReloadAsync(string songId)
    {
        _database.Context.ChangeTracker.Clear();
        return await _database.Context.Songs.AsNoTracking().SingleAsync(s => s.SongId == songId);
    }

    [Fact]
    public async Task OwnedAuditionSong_IsPromoted_AndReportedKept()
    {
        var user = await SeedUserAsync();
        var song = await SeedSongAsync(exploratory: true, owner: user);

        var result = await RunAsync(user.UserId, song.SongId);

        Assert.True(result.IsOk);
        var item = Assert.Single(result.Get().Results);
        Assert.Equal(new SongKeepResult(song.SongId, KeepOutcomes.Kept), item);

        var reloaded = await ReloadAsync(song.SongId);
        Assert.False(reloaded.Exploratory);
        Assert.True(reloaded.Version > OldVersion); // clients pick the promotion up via /songs/changed
        Assert.Null(reloaded.TagModel); // still findable by the stale-tag work query
    }

    [Fact]
    public async Task SongNotInCallersLibrary_FailsThatItem_AndIsLeftExploratory()
    {
        var caller = await SeedUserAsync();
        var other = await SeedUserAsync();
        var mine = await SeedSongAsync(exploratory: true, owner: caller);
        var theirs = await SeedSongAsync(exploratory: true, owner: other);

        var result = await RunAsync(caller.UserId, theirs.SongId, mine.SongId);

        Assert.True(result.IsOk);
        Assert.Collection(result.Get().Results,
            r =>
            {
                Assert.Equal(theirs.SongId, r.SongId);
                Assert.Equal(KeepOutcomes.Failed, r.Outcome);
                Assert.Equal("Song is not in your library.", r.Error);
            },
            r => Assert.Equal(new SongKeepResult(mine.SongId, KeepOutcomes.Kept), r));

        Assert.True((await ReloadAsync(theirs.SongId)).Exploratory);
        Assert.False((await ReloadAsync(mine.SongId)).Exploratory);
    }

    [Fact]
    public async Task UnknownSongId_FailsThatItem()
    {
        var user = await SeedUserAsync();

        var result = await RunAsync(user.UserId, "no-such-song");

        Assert.True(result.IsOk);
        var item = Assert.Single(result.Get().Results);
        Assert.Equal(KeepOutcomes.Failed, item.Outcome);
    }

    [Fact]
    public async Task AlreadyPromotedSong_IsKept_WithoutChurningItsVersion()
    {
        var user = await SeedUserAsync();
        var song = await SeedSongAsync(exploratory: false, owner: user);

        var result = await RunAsync(user.UserId, song.SongId);

        Assert.True(result.IsOk);
        Assert.Equal(KeepOutcomes.Kept, Assert.Single(result.Get().Results).Outcome);
        Assert.Equal(OldVersion, (await ReloadAsync(song.SongId)).Version);
    }

    [Fact]
    public async Task DuplicateAndBlankIds_CollapseInFirstOccurrenceOrder()
    {
        var user = await SeedUserAsync();
        var a = await SeedSongAsync(exploratory: true, owner: user);
        var b = await SeedSongAsync(exploratory: true, owner: user);

        var result = await RunAsync(user.UserId, a.SongId, " ", b.SongId, a.SongId, "");

        Assert.True(result.IsOk);
        Assert.Equal([a.SongId, b.SongId], result.Get().Results.Select(r => r.SongId));
    }

    [Fact]
    public async Task EmptyBatch_IsOk_AndTouchesNothing()
    {
        var spy = new SpyPromotion();

        var result = await new KeepSongsHandler(_database.UnitOfWork, spy)
            .Handle(new KeepSongsCommand("u", []), CancellationToken.None);

        Assert.True(result.IsOk);
        Assert.Empty(result.Get().Results);
        Assert.Empty(spy.Calls);
    }

    [Fact]
    public async Task OverTheBatchCap_IsRejected_BeforeAnyWrite()
    {
        var spy = new SpyPromotion();
        var ids = Enumerable.Range(0, KeepSongsHandler.MaxBatch + 1).Select(i => $"s{i}").ToArray();

        var result = await new KeepSongsHandler(_database.UnitOfWork, spy)
            .Handle(new KeepSongsCommand("u", ids), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Empty(spy.Calls);
    }

    [Fact]
    public async Task PromotionFailure_FailsTheWholeBatch_SoTheOutboxRetries()
    {
        // Per-item "failed" would read as a verdict on the song; a batch error keeps the app's outbox rows.
        var user = await SeedUserAsync();
        var song = await SeedSongAsync(exploratory: true, owner: user);
        var spy = new SpyPromotion(Result.Error<int>("database is locked"));

        var result = await new KeepSongsHandler(_database.UnitOfWork, spy)
            .Handle(new KeepSongsCommand(user.UserId, [song.SongId]), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("database is locked", result.GetError().Message);
    }

    [Fact]
    public async Task OnlyOwnedIds_AreHandedToPromotion()
    {
        var caller = await SeedUserAsync();
        var other = await SeedUserAsync();
        var mine = await SeedSongAsync(exploratory: true, owner: caller);
        var theirs = await SeedSongAsync(exploratory: true, owner: other);
        var spy = new SpyPromotion();

        await new KeepSongsHandler(_database.UnitOfWork, spy)
            .Handle(new KeepSongsCommand(caller.UserId, [theirs.SongId, mine.SongId]), CancellationToken.None);

        Assert.Equal([mine.SongId], Assert.Single(spy.Calls));
    }
}
