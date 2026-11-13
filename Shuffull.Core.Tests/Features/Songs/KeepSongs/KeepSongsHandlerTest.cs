using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nut.Results;
using Shuffull.Api.Services;
using Shuffull.Core.Features.Songs.KeepSongs;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Alerts;
using Shuffull.Core.Errors;
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
    private readonly RecordingHealthSignals _signals = new();

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

    /// <summary>Records what was kept, and for whom, and can be scripted to fail (a down/locked database).</summary>
    private sealed class SpyPromotion(Result<int>? scripted = null) : IAuditionPromotionService
    {
        public List<(string UserId, IReadOnlyList<string> SongIds)> Calls { get; } = [];
        public Task<Result<int>> PromoteAsync(IReadOnlyList<string> songIds, CancellationToken cancellationToken = default!)
            => throw new InvalidOperationException("A Keep goes through KeepAsync, which records its time.");
        public Task<Result<int>> KeepAsync(string userId, IReadOnlyList<string> songIds, CancellationToken cancellationToken = default!)
        {
            Calls.Add((userId, songIds));
            return Task.FromResult(scripted ?? Result.Ok(songIds.Count));
        }
    }

    /// <summary>
    /// Runs <paramref name="before"/> and then the real promotion, standing in for a write that commits between the
    /// handler's ownership read and its promotion.
    /// </summary>
    private sealed class InterleavedPromotion(IAuditionPromotionService inner, Func<Task> before) : IAuditionPromotionService
    {
        public async Task<Result<int>> PromoteAsync(IReadOnlyList<string> songIds, CancellationToken cancellationToken = default!)
        {
            await before();
            return await inner.PromoteAsync(songIds, cancellationToken);
        }

        // The handler keeps through KeepAsync, so this is the call the interleaved write has to precede.
        public async Task<Result<int>> KeepAsync(string userId, IReadOnlyList<string> songIds, CancellationToken cancellationToken = default!)
        {
            await before();
            return await inner.KeepAsync(userId, songIds, cancellationToken);
        }
    }

    private Task<Result<KeepSongsResponse>> RunAsync(string userId, params string[] songIds)
        => new KeepSongsHandler(_database.UnitOfWork, _promotion, _signals)
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
    public async Task RefusedKeeps_AreRecordedOnePerSong_ForTheOwnersAlert()
    {
        var caller = await SeedUserAsync();
        var other = await SeedUserAsync();
        var first = await SeedSongAsync(exploratory: true, owner: other);
        var second = await SeedSongAsync(exploratory: true, owner: other);
        var mine = await SeedSongAsync(exploratory: true, owner: caller);

        await RunAsync(caller.UserId, first.SongId, mine.SongId, second.SongId);

        Assert.Equal(
            [ShuffullSignalRules.KeepRefusedKey(caller.UserId, first.SongId), ShuffullSignalRules.KeepRefusedKey(caller.UserId, second.SongId)],
            _signals.Failures.Select(f => f.Key).ToList());
        var (_, error) = _signals.Failures[0];
        Assert.Contains(caller.UserId, error);
        Assert.Contains(first.SongId, error);
        Assert.Contains("Song is not in your library.", error);
        Assert.Empty(_signals.Successes); // an event signal: nothing ever clears it early
    }

    [Fact]
    public async Task KeptSongs_RecordNoRefusal()
    {
        var user = await SeedUserAsync();
        var song = await SeedSongAsync(exploratory: true, owner: user);

        await RunAsync(user.UserId, song.SongId);

        Assert.Empty(_signals.Failures);
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

        var result = await new KeepSongsHandler(_database.UnitOfWork, spy, _signals)
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

        var result = await new KeepSongsHandler(_database.UnitOfWork, spy, _signals)
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
        var spy = new SpyPromotion(Result.Error<int>(new KindedError(ErrorKind.ServerFault, "database is locked")));

        var result = await new KeepSongsHandler(_database.UnitOfWork, spy, _signals)
            .Handle(new KeepSongsCommand(user.UserId, [song.SongId]), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("database is locked", result.GetError().Message);
        // A 5xx, which the app retries. As a 400 the Keep was dead-lettered after 7 days (Shuffull#41).
        Assert.Equal(ErrorKind.ServerFault, ResultErrors.KindOf(result.GetError()));
    }

    [Fact]
    public async Task OnlyOwnedIds_AreHandedToPromotion()
    {
        var caller = await SeedUserAsync();
        var other = await SeedUserAsync();
        var mine = await SeedSongAsync(exploratory: true, owner: caller);
        var theirs = await SeedSongAsync(exploratory: true, owner: other);
        var spy = new SpyPromotion();

        await new KeepSongsHandler(_database.UnitOfWork, spy, _signals)
            .Handle(new KeepSongsCommand(caller.UserId, [theirs.SongId, mine.SongId]), CancellationToken.None);

        var call = Assert.Single(spy.Calls);
        Assert.Equal(caller.UserId, call.UserId);
        Assert.Equal([mine.SongId], call.SongIds);
    }

    private async Task<UserSong> ReloadUserSongAsync(string userId, string songId)
    {
        _database.Context.ChangeTracker.Clear();
        return await _database.Context.UserSongs.AsNoTracking().SingleAsync(us => us.UserId == userId && us.SongId == songId);
    }

    [Fact]
    public async Task Keep_RecordsItsTime_OnTheCallersUserSongOnly()
    {
        var caller = await SeedUserAsync();
        var other = await SeedUserAsync();
        var song = await SeedSongAsync(exploratory: true, owner: caller);
        await _database.Context.UserSongs.AddAsync(new UserSong
        {
            UserId = other.UserId, SongId = song.SongId, LastPlayed = DateTime.MinValue, Version = OldVersion,
        });
        await _database.Context.SaveChangesAsync();
        var before = DateTime.UtcNow;

        await RunAsync(caller.UserId, song.SongId);

        var mine = await ReloadUserSongAsync(caller.UserId, song.SongId);
        Assert.NotNull(mine.KeptAt);
        Assert.InRange(mine.KeptAt!.Value, before.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1));
        Assert.Equal(OldVersion, mine.Version); // KeptAt isn't synced, so the row's sync cursor stays put
        Assert.Null((await ReloadUserSongAsync(other.UserId, song.SongId)).KeptAt);
    }

    [Fact]
    public async Task Keep_OfASongAnotherUserAlreadyPromoted_StillRecordsThisUsersTime()
    {
        var user = await SeedUserAsync();
        var song = await SeedSongAsync(exploratory: false, owner: user);

        await RunAsync(user.UserId, song.SongId);

        Assert.NotNull((await ReloadUserSongAsync(user.UserId, song.SongId)).KeptAt);
        Assert.Equal(OldVersion, (await ReloadAsync(song.SongId)).Version);
    }

    [Fact]
    public async Task ResentKeep_KeepsTheFirstTime()
    {
        var user = await SeedUserAsync();
        var song = await SeedSongAsync(exploratory: true, owner: user);
        await RunAsync(user.UserId, song.SongId);
        var first = (await ReloadUserSongAsync(user.UserId, song.SongId)).KeptAt;

        await Task.Delay(20);
        await RunAsync(user.UserId, song.SongId);

        Assert.Equal(first, (await ReloadUserSongAsync(user.UserId, song.SongId)).KeptAt);
    }

    [Fact]
    public async Task LinkDroppedBeforeThePromotion_IsReportedNotInYourLibrary()
    {
        // A mix delete on another device drops the caller's link to a shared audition song after the handler read it
        // (Shuffull#87). The song survives for the other user, so the promotion succeeds, but the caller no longer
        // has it, so the Keep must not come back as Kept.
        var caller = await SeedUserAsync();
        var other = await SeedUserAsync();
        var song = await SeedSongAsync(exploratory: true, owner: caller);
        await _database.Context.UserSongs.AddAsync(new UserSong
        {
            UserId = other.UserId,
            SongId = song.SongId,
            LastPlayed = DateTime.MinValue,
            Version = OldVersion,
        });
        await _database.Context.SaveChangesAsync();

        var promotion = new InterleavedPromotion(_promotion, async () =>
        {
            await using var context = _database.CreateContext();
            await context.UserSongs
                .Where(us => us.UserId == caller.UserId && us.SongId == song.SongId)
                .ExecuteDeleteAsync();
        });

        var result = await new KeepSongsHandler(_database.UnitOfWork, promotion, _signals)
            .Handle(new KeepSongsCommand(caller.UserId, [song.SongId]), CancellationToken.None);

        Assert.True(result.IsOk);
        Assert.Equal(
            new SongKeepResult(song.SongId, KeepOutcomes.Failed, "Song is not in your library."),
            Assert.Single(result.Get().Results));
    }
}
