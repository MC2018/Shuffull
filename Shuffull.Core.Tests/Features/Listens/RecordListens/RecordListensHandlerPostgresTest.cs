using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Features.Listens.RecordListens;
using Shuffull.Core.Features.Playlists.DeletePlaylist;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Features.Listens.RecordListens;

/// <summary>
/// The listen log on real Postgres, built by the real migrations (so this also proves AddListenLog applies). Covers
/// what in-memory SQLite can't: two copies of one batch in flight at once, where the second must wait for the first
/// and come back "duplicate" rather than fail the key. Skipped unless SHUFFULL_TEST_POSTGRES is set.
/// </summary>
public class RecordListensHandlerPostgresTest : IAsyncLifetime
{
    private static readonly DateTime T0 = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private PostgresDatabase? _database;

    private PostgresDatabase Database => _database ?? throw new InvalidOperationException("Postgres is not configured.");

    public async Task InitializeAsync()
    {
        if (PostgresFactAttribute.BaseConnectionString is { Length: > 0 } baseConnectionString)
        {
            _database = await PostgresDatabase.CreateAsync(baseConnectionString);
        }
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }

    private async Task<User> SeedUserAsync()
    {
        await using var context = Database.CreateContext();
        var user = new User { UserId = Guid.NewGuid().ToString(), Username = $"user-{Guid.NewGuid():N}", Version = T0, ServerHash = "server-hash" };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    private static ListenItem Item(string listenId, string songId = "song-1") =>
        new(listenId, songId, T0, T0.AddSeconds(30), 30, 200, "skipped", "playlist", "playlist-1", "android");

    private async Task<Result<RecordListensResponse>> RunAsync(ShuffullContext context, string userId, params ListenItem[] listens)
        => await new RecordListensHandler(context).Handle(new RecordListensCommand(userId, listens), CancellationToken.None);

    private async Task<Result<RecordListensResponse>> RunAsync(string userId, params ListenItem[] listens)
    {
        await using var context = Database.CreateContext();
        return await RunAsync(context, userId, listens);
    }

    [PostgresFact]
    public async Task Record_NewDuplicateForeignAndNullFields_OnPostgres()
    {
        var me = await SeedUserAsync();
        var other = await SeedUserAsync();
        Assert.True((await RunAsync(other.UserId, Item("theirs"))).IsOk);

        var first = await RunAsync(me.UserId,
            Item("new") with { SongDurationSeconds = null, PlaylistId = null, Platform = null },
            Item("theirs"),
            Item("names-other") with { UserId = other.UserId });
        var resend = await RunAsync(me.UserId, Item("new"));

        Assert.Equal(
            [ListenOutcomes.Recorded, ListenOutcomes.Rejected, ListenOutcomes.Rejected],
            first.Get().Results.Select(r => r.Outcome));
        Assert.Equal(ListenOutcomes.Duplicate, Assert.Single(resend.Get().Results).Outcome);

        await using var check = Database.CreateContext();
        var stored = await check.Listens.SingleAsync(l => l.ListenId == "new");
        Assert.Equal((me.UserId, T0, (double?)null, (string?)null), (stored.UserId, stored.StartedAt, stored.SongDurationSeconds, stored.PlaylistId));
        Assert.Equal(2, await check.Listens.CountAsync());
    }

    [PostgresFact]
    public async Task Record_SameBatchSentTwiceAtOnce_SecondWaitsThenReportsDuplicate()
    {
        var me = await SeedUserAsync();

        // The first copy has inserted its rows and not yet committed.
        await using var first = Database.CreateContext();
        await using var firstTransaction = await first.Database.BeginTransactionAsync();
        await first.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Listens" ("ListenId", "UserId", "SongId", "StartedAt", "EndedAt", "SecondsListened", "EndReason", "Context", "RecordedAt")
            VALUES ('a', {me.UserId}, 'song-1', {T0}, {T0}, 30, 1, 0, {T0})
            """);

        await using var second = Database.CreateContext();
        var secondSend = RunAsync(second, me.UserId, Item("a"), Item("b"));

        Assert.True(await Database.WaitForLockWaiterAsync());
        Assert.False(secondSend.IsCompleted);
        await firstTransaction.CommitAsync();

        var result = await secondSend;
        Assert.True(result.IsOk, result.IsError ? result.GetError().Message : null);
        Assert.Equal(
            [("a", ListenOutcomes.Duplicate), ("b", ListenOutcomes.Recorded)],
            result.Get().Results.Select(r => (r.ListenId, r.Outcome)));
        await using var check = Database.CreateContext();
        Assert.Equal(2, await check.Listens.CountAsync());
    }

    [PostgresFact]
    public async Task Record_FirstCopyRollsBack_SecondRecordsIt()
    {
        var me = await SeedUserAsync();

        await using var first = Database.CreateContext();
        await using var firstTransaction = await first.Database.BeginTransactionAsync();
        await first.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Listens" ("ListenId", "UserId", "SongId", "StartedAt", "EndedAt", "SecondsListened", "EndReason", "Context", "RecordedAt")
            VALUES ('a', {me.UserId}, 'song-1', {T0}, {T0}, 30, 1, 0, {T0})
            """);

        await using var second = Database.CreateContext();
        var secondSend = RunAsync(second, me.UserId, Item("a"));

        Assert.True(await Database.WaitForLockWaiterAsync());
        await firstTransaction.RollbackAsync();

        Assert.Equal(ListenOutcomes.Recorded, Assert.Single((await secondSend).Get().Results).Outcome);
    }

    [PostgresFact]
    public async Task PurgedAuditionSong_KeepsItsListens_OnPostgres()
    {
        var me = await SeedUserAsync();
        await using (var seed = Database.CreateContext())
        {
            seed.Playlists.Add(new Playlist { PlaylistId = "mix", UserId = me.UserId, Name = "Mix", PercentUntilReplayable = 0.5m, IsExploratory = true, Version = T0 });
            seed.Songs.Add(new Song { SongId = "audition-song", Name = "Song", FileExtension = ".mp3", FileHash = "hash", Exploratory = true, Version = T0 });
            seed.PlaylistSongs.Add(new PlaylistSong { PlaylistSongId = "ps", PlaylistId = "mix", SongId = "audition-song" });
            await seed.SaveChangesAsync();
        }
        Assert.True((await RunAsync(me.UserId, Item("heard-it", songId: "audition-song") with { Context = "audition", PlaylistId = "mix" })).IsOk);

        await using (var purge = Database.CreateContext())
        {
            var deleted = await new DeletePlaylistHandler(purge).Handle(new DeletePlaylistCommand(me.UserId, "mix"), CancellationToken.None);
            Assert.True(deleted.IsOk, deleted.IsError ? deleted.GetError().Message : null);
            Assert.Equal(["audition-song"], deleted.Get().PurgedSongIds);
        }

        await using var check = Database.CreateContext();
        Assert.False(await check.Songs.AnyAsync(s => s.SongId == "audition-song"));
        Assert.Equal("audition-song", (await check.Listens.SingleAsync()).SongId);
    }
}
