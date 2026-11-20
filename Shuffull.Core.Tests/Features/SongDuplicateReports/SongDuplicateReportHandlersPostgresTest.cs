using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Features.SongDuplicateReports.GetMySongDuplicateReports;
using Shuffull.Core.Features.SongDuplicateReports.GetSongDuplicateQueue;
using Shuffull.Core.Features.SongDuplicateReports.ReportSongDuplicates;
using Shuffull.Core.Features.SongDuplicateReports.WithdrawSongDuplicateReport;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Features.SongDuplicateReports;

/// <summary>
/// Duplicate reports on real Postgres, built by the real migrations (so this also proves AddSongDuplicateReports
/// applies): the guarantees SQLite can't show. Two rows on one user's pair fail the unique index as a conflict, a
/// write that read a report before a withdraw fails instead of overwriting it, the Version a write returns is the one
/// the feed pages on, and the queue's grouping runs in SQL. Skipped unless SHUFFULL_TEST_POSTGRES is set.
/// </summary>
public class SongDuplicateReportHandlersPostgresTest : IAsyncLifetime
{
    private static readonly DateTime T0 = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

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

    /// <summary>A user who owns <paramref name="songCount"/> songs.</summary>
    private async Task<(User User, List<Song> Songs)> SeedAsync(int songCount = 2)
    {
        await using var context = Database.CreateContext();
        var user = new User { UserId = Guid.NewGuid().ToString(), Username = $"user-{Guid.NewGuid():N}", Version = T0, ServerHash = "server-hash" };
        context.Users.Add(user);
        var songs = Enumerable.Range(0, songCount)
            .Select(i => new Song { SongId = Guid.NewGuid().ToString(), Name = $"Song {i}", FileExtension = ".mp3", FileHash = Guid.NewGuid().ToString(), Version = T0 })
            .ToList();
        context.Songs.AddRange(songs);
        context.UserSongs.AddRange(songs.Select(s => new UserSong { UserId = user.UserId, SongId = s.SongId, LastPlayed = DateTime.MinValue, Version = T0 }));
        await context.SaveChangesAsync();
        return (user, songs);
    }

    private async Task<ReportSongDuplicatesResponse> ReportAsync(string userId, params SongDuplicateReportItem[] items)
    {
        await using var context = Database.CreateContext();
        var result = await new ReportSongDuplicatesHandler(context).Handle(new ReportSongDuplicatesCommand(userId, items), CancellationToken.None);
        Assert.True(result.IsOk, result.IsError ? result.GetError().Message : null);
        return result.Get();
    }

    private static SongDuplicateReportItem Item(string reportId, Song song, Song? other)
        => new(reportId, song.SongId, other?.SongId, other is null ? "differentVersion" : "duplicate", null, null);

    [PostgresFact]
    public async Task Report_FoldsASecondIdIntoAnAlias_AndTheReturnedVersionIsTheOneTheFeedPagesOn()
    {
        var (me, songs) = await SeedAsync();

        // Both in one batch: the alias row has to be inserted after the report it points at.
        var response = await ReportAsync(me.UserId, Item("first", songs[0], songs[1]), Item("second", songs[1], songs[0]));

        Assert.Equal(["recorded", "updated"], response.Results.Select(r => r.Outcome));
        var returned = response.Results[1].Report!;
        await using var context = Database.CreateContext();
        var mine = await new GetMySongDuplicateReportsHandler(context)
            .Handle(new GetMySongDuplicateReportsQuery(me.UserId, DateTime.MinValue), CancellationToken.None);
        Assert.Equal(returned.Version, Assert.Single(mine.Get().Reports).Version);
        var after = await new GetMySongDuplicateReportsHandler(context)
            .Handle(new GetMySongDuplicateReportsQuery(me.UserId, returned.Version, returned.ReportId), CancellationToken.None);
        Assert.Empty(after.Get().Reports);
        Assert.Equal("first", await context.SongDuplicateReportAliases.Where(a => a.AliasId == "second").Select(a => a.ReportId).SingleAsync());
    }

    [PostgresFact]
    public async Task ASecondRowOnTheSameUsersPair_FailsTheUniqueIndexAsAConflict()
    {
        // What a second device creating the same pair at the same moment runs into: both read "no report yet".
        var (me, songs) = await SeedAsync();
        await ReportAsync(me.UserId, Item("device-a", songs[0], songs[1]));

        await using var context = Database.CreateContext();
        context.SongDuplicateReports.Add(new SongDuplicateReport
        {
            ReportId = "device-b", ReporterUserId = me.UserId, SongId = songs[1].SongId, OtherSongId = songs[0].SongId,
            Kind = SongDuplicateReportKind.Duplicate, HiddenSongId = songs[1].SongId,
            PairKey = SongDuplicateReport.PairKeyOf(songs[1].SongId, songs[0].SongId), CreatedAt = T0, Version = T0,
        });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.Equal(ErrorKind.Conflict, ResultErrors.FromException(error, "save").Kind);
    }

    [PostgresFact]
    public async Task AWriteThatReadTheReportBeforeAWithdraw_FailsInsteadOfOverwritingIt()
    {
        // The resolve's path: load the pending rows, then save them. A withdraw lands in between.
        var (me, songs) = await SeedAsync();
        await ReportAsync(me.UserId, Item("r1", songs[0], songs[1]));
        await using var resolving = Database.CreateContext();
        var loaded = await resolving.SongDuplicateReports.SingleAsync();

        await using (var withdrawing = Database.CreateContext())
        {
            var withdrawn = await new WithdrawSongDuplicateReportHandler(withdrawing)
                .Handle(new WithdrawSongDuplicateReportCommand(me.UserId, "r1"), CancellationToken.None);
            Assert.True(withdrawn.IsOk);
        }
        loaded.Status = SongDuplicateReportStatus.KeptBoth;
        loaded.Version = T0.AddDays(30);
        var error = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => resolving.SaveChangesAsync());

        Assert.Equal(ErrorKind.Conflict, ResultErrors.FromException(error, "save").Kind);
        await using var check = Database.CreateContext();
        Assert.Equal(SongDuplicateReportStatus.Withdrawn, (await check.SongDuplicateReports.SingleAsync()).Status);
    }

    [PostgresFact]
    public async Task Queue_GroupsAndPagesInSql_AndSkipsAPairWithAMissingSong()
    {
        var (me, songs) = await SeedAsync(4);
        var (other, _) = await SeedAsync(0);
        await using (var context = Database.CreateContext())
        {
            context.UserSongs.AddRange(songs.Take(2).Select(s => new UserSong { UserId = other.UserId, SongId = s.SongId, LastPlayed = DateTime.MinValue, Version = T0 }));
            await context.SaveChangesAsync();
        }
        await ReportAsync(me.UserId, Item("mine-1", songs[0], songs[1]), Item("mine-2", songs[2], songs[3]), Item("lone", songs[0], null));
        await ReportAsync(other.UserId, Item("theirs-1", songs[1], songs[0]));
        await using (var context = Database.CreateContext())
        {
            await context.UserSongs.Where(us => us.SongId == songs[3].SongId).ExecuteDeleteAsync();
            await context.Songs.Where(s => s.SongId == songs[3].SongId).ExecuteDeleteAsync();
        }

        await using var queueContext = Database.CreateContext();
        var queue = (await new GetSongDuplicateQueueHandler(queueContext).Handle(new GetSongDuplicateQueueQuery(), CancellationToken.None)).Get();

        var pair = Assert.Single(queue.Pairs);
        Assert.Equal(0, queue.Remaining);
        Assert.Equal(2, pair.ReportCount);
        Assert.Equal(new[] { songs[0].SongId, songs[1].SongId }.Order(StringComparer.Ordinal), new[] { pair.FirstSong.SongId, pair.SecondSong.SongId });
    }
}
