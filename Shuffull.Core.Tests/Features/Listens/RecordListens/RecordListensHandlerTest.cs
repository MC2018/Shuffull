using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Features.Listens.RecordListens;
using Shuffull.Core.Features.Playlists.DeletePlaylist;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Features.Listens.RecordListens;

/// <summary>
/// The listen log's batch write against a real database: per-item outcomes, resends that write nothing, validation
/// that refuses one item rather than the batch, and listens that outlive their song.
/// </summary>
public class RecordListensHandlerTest : IDisposable
{
    private static readonly DateTime T0 = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly DatabaseFixture _database = new();

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<Result<RecordListensResponse>> RunAsync(string userId, params ListenItem[] listens)
    {
        await using var context = _database.CreateContext();
        return await new RecordListensHandler(context).Handle(new RecordListensCommand(userId, listens), CancellationToken.None);
    }

    private async Task<User> SeedUserAsync()
    {
        var user = new User { UserId = Guid.NewGuid().ToString(), Username = $"user-{Guid.NewGuid():N}", Version = T0, ServerHash = "server-hash" };
        _database.Context.Users.Add(user);
        await _database.Context.SaveChangesAsync();
        return user;
    }

    private static ListenItem Item(string listenId, string songId = "song-1", double seconds = 30) => new(
        ListenId: listenId,
        SongId: songId,
        StartedAt: T0,
        EndedAt: T0.AddSeconds(seconds),
        SecondsListened: seconds,
        SongDurationSeconds: 200,
        EndReason: "skipped",
        Context: "playlist",
        PlaylistId: "playlist-1",
        Platform: "android");

    private async Task<List<string>> StoredIdsAsync()
    {
        await using var context = _database.CreateContext();
        return await context.Listens.Select(l => l.ListenId).OrderBy(id => id).ToListAsync();
    }

    private static List<(string? ListenId, string Outcome)> Outcomes(Result<RecordListensResponse> result)
        => result.Get().Results.Select(r => (r.ListenId, r.Outcome)).ToList();

    [Fact]
    public async Task Record_NewDuplicateAndForeignUserItems_ReturnsEachItemsOutcome()
    {
        var me = await SeedUserAsync();
        var other = await SeedUserAsync();
        Assert.True((await RunAsync(me.UserId, Item("mine-before"))).IsOk);
        Assert.True((await RunAsync(other.UserId, Item("theirs"))).IsOk);

        var result = await RunAsync(me.UserId,
            Item("new"),
            Item("mine-before"),
            Item("names-other-user") with { UserId = other.UserId },
            Item("names-me") with { UserId = me.UserId },
            Item("theirs"));

        Assert.True(result.IsOk);
        Assert.Equal(
            [
                ("new", ListenOutcomes.Recorded),
                ("mine-before", ListenOutcomes.Duplicate),
                ("names-other-user", ListenOutcomes.Rejected),
                ("names-me", ListenOutcomes.Recorded),
                ("theirs", ListenOutcomes.Rejected),
            ],
            Outcomes(result));
        Assert.All(result.Get().Results.Where(r => r.Outcome == ListenOutcomes.Rejected), r => Assert.NotNull(r.Error));

        await using var check = _database.CreateContext();
        Assert.Equal(other.UserId, (await check.Listens.SingleAsync(l => l.ListenId == "theirs")).UserId);
        Assert.Equal(["mine-before", "names-me", "new", "theirs"], await StoredIdsAsync());
    }

    [Fact]
    public async Task Record_SameBatchResent_WritesNothingNew()
    {
        var me = await SeedUserAsync();
        ListenItem[] batch = [Item("a"), Item("b", seconds: 200) with { EndReason = "finished" }];
        Assert.True((await RunAsync(me.UserId, batch)).IsOk);

        // A resend that differs is still the same listen: the first copy stays as it was.
        var resend = await RunAsync(me.UserId, batch[0] with { SecondsListened = 999 }, batch[1]);

        Assert.Equal([("a", ListenOutcomes.Duplicate), ("b", ListenOutcomes.Duplicate)], Outcomes(resend));
        await using var check = _database.CreateContext();
        Assert.Equal(2, await check.Listens.CountAsync());
        Assert.Equal(30, (await check.Listens.SingleAsync(l => l.ListenId == "a")).SecondsListened);
    }

    [Fact]
    public async Task Record_SameIdTwiceInOneBatch_RecordsTheFirstAndReportsTheSecondDuplicate()
    {
        var me = await SeedUserAsync();

        var result = await RunAsync(me.UserId, Item("a", seconds: 10), Item("a", seconds: 20));

        Assert.Equal([("a", ListenOutcomes.Recorded), ("a", ListenOutcomes.Duplicate)], Outcomes(result));
        await using var check = _database.CreateContext();
        Assert.Equal(10, (await check.Listens.SingleAsync()).SecondsListened);
    }

    [Fact]
    public async Task Record_InvalidItems_AreRejectedOneByOne_AndTheRestRecorded()
    {
        var me = await SeedUserAsync();

        var result = await RunAsync(me.UserId,
            Item("negative-seconds") with { SecondsListened = -1 },
            Item("ends-before-start") with { EndedAt = T0.AddSeconds(-1) },
            Item("unknown-end-reason") with { EndReason = "exploded" },
            Item("numeric-end-reason") with { EndReason = "1" },
            Item("unknown-context") with { Context = "radio" },
            Item("no-context") with { Context = null },
            Item("no-start") with { StartedAt = null },
            Item("no-seconds") with { SecondsListened = null },
            Item("negative-duration") with { SongDurationSeconds = -5 },
            Item("no-song") with { SongId = " " },
            Item(new string('x', Listen.MaxIdLength + 1)),
            Item(""),
            Item("future") with { StartedAt = DateTime.UtcNow.AddDays(3), EndedAt = DateTime.UtcNow.AddDays(3) },
            Item("good"));

        Assert.True(result.IsOk);
        var outcomes = result.Get().Results;
        Assert.Equal(14, outcomes.Count);
        Assert.All(outcomes.Take(13), r => Assert.Equal(ListenOutcomes.Rejected, r.Outcome));
        Assert.All(outcomes.Take(13), r => Assert.False(string.IsNullOrWhiteSpace(r.Error)));
        Assert.Equal(ListenOutcomes.Recorded, outcomes[13].Outcome);
        Assert.Equal(["good"], await StoredIdsAsync());
    }

    [Fact]
    public async Task Record_FieldsAreStored_EnumNamesIgnoreCase_AndTimesAreUtc()
    {
        var me = await SeedUserAsync();
        var startedLocal = new DateTimeOffset(2026, 10, 1, 14, 0, 0, TimeSpan.FromHours(2));

        var result = await RunAsync(me.UserId, new ListenItem(
            ListenId: "a",
            SongId: "song-1",
            StartedAt: startedLocal.LocalDateTime,
            EndedAt: DateTime.SpecifyKind(new DateTime(2026, 10, 1, 12, 3, 0), DateTimeKind.Unspecified),
            SecondsListened: 175.5,
            SongDurationSeconds: null,
            EndReason: "FINISHED",
            Context: "genreJam",
            PlaylistId: " ",
            Platform: "desktop"));

        Assert.Equal([("a", ListenOutcomes.Recorded)], Outcomes(result));
        await using var check = _database.CreateContext();
        var stored = await check.Listens.SingleAsync();
        Assert.Equal(me.UserId, stored.UserId);
        Assert.Equal("song-1", stored.SongId);
        Assert.Equal(T0, stored.StartedAt);
        Assert.Equal(T0.AddMinutes(3), stored.EndedAt);
        Assert.Equal(175.5, stored.SecondsListened);
        Assert.Null(stored.SongDurationSeconds);
        Assert.Equal(ListenEndReason.Finished, stored.EndReason);
        Assert.Equal(ListenContext.GenreJam, stored.Context);
        Assert.Null(stored.PlaylistId);
        Assert.Equal("desktop", stored.Platform);
        Assert.True(stored.RecordedAt > T0);
    }

    [Fact]
    public async Task Record_SongNotInLibraryOrGone_IsStillRecorded_AndNoUserSongIsTouched()
    {
        var me = await SeedUserAsync();

        var result = await RunAsync(me.UserId, Item("a", songId: "song-nobody-has"));

        Assert.Equal([("a", ListenOutcomes.Recorded)], Outcomes(result));
        await using var check = _database.CreateContext();
        Assert.Empty(await check.UserSongs.ToListAsync());
    }

    [Fact]
    public async Task Record_FullOutboxBatch_IsAccepted_AndOneMoreFailsTheBatch()
    {
        var me = await SeedUserAsync();
        var full = Enumerable.Range(0, RecordListensHandler.MaxBatch).Select(i => Item($"l{i:D3}")).ToArray();

        var accepted = await RunAsync(me.UserId, full);
        var tooMany = await RunAsync(me.UserId, [.. full, Item("one-more")]);

        Assert.True(RecordListensHandler.MaxBatch >= 200);
        Assert.All(accepted.Get().Results, r => Assert.Equal(ListenOutcomes.Recorded, r.Outcome));
        Assert.True(tooMany.IsError);
        Assert.Equal(RecordListensHandler.MaxBatch, (await StoredIdsAsync()).Count);
    }

    [Fact]
    public async Task Record_EmptyBatch_IsOkAndWritesNothing()
    {
        var me = await SeedUserAsync();

        var result = await RunAsync(me.UserId);

        Assert.Empty(result.Get().Results);
        Assert.Empty(await StoredIdsAsync());
    }

    [Fact]
    public async Task PurgedAuditionSong_KeepsItsListens()
    {
        var me = await SeedUserAsync();
        var playlist = new Playlist { PlaylistId = "mix", UserId = me.UserId, Name = "Mix", PercentUntilReplayable = 0.5m, IsExploratory = true, Version = T0 };
        var song = new Song { SongId = "audition-song", Name = "Song", FileExtension = ".mp3", FileHash = "hash", Exploratory = true, Version = T0 };
        _database.Context.AddRange(playlist, song, new PlaylistSong { PlaylistSongId = "ps", PlaylistId = "mix", SongId = song.SongId });
        await _database.Context.SaveChangesAsync();
        Assert.True((await RunAsync(me.UserId, Item("heard-it", songId: song.SongId) with { Context = "audition", PlaylistId = "mix" })).IsOk);

        await using (var purge = _database.CreateContext())
        {
            var deleted = await new DeletePlaylistHandler(purge).Handle(new DeletePlaylistCommand(me.UserId, "mix"), CancellationToken.None);
            Assert.Equal([song.SongId], deleted.Get().PurgedSongIds);
        }

        await using var check = _database.CreateContext();
        Assert.False(await check.Songs.AnyAsync(s => s.SongId == song.SongId));
        var listen = await check.Listens.SingleAsync();
        Assert.Equal((song.SongId, "mix"), (listen.SongId, listen.PlaylistId));
    }
}
