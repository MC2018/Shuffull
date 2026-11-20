using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Features.SongDuplicateReports.GetMySongDuplicateReports;
using Shuffull.Core.Features.SongDuplicateReports.GetSongDuplicateQueue;
using Shuffull.Core.Features.SongDuplicateReports.ReportSongDuplicates;
using Shuffull.Core.Features.SongDuplicateReports.ResolveSongDuplicateReports;
using Shuffull.Core.Features.SongDuplicateReports.WithdrawSongDuplicateReport;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Features.SongDuplicateReports;

/// <summary>
/// Duplicate reports against a real database (shuffull-workspace#143, Shuffull#141): per-item outcomes, one report per
/// user per pair, the hidden copy, the reporter's paged feed, withdraw, and a curator's resolve and queue. A report
/// never touches the reporter's like status.
/// </summary>
public class SongDuplicateReportHandlersTest : IDisposable
{
    private static readonly DateTime T0 = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly DatabaseFixture _database = new();

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<User> SeedUserAsync(bool curator = false)
    {
        var user = new User
        {
            UserId = Guid.NewGuid().ToString(), Username = $"user-{Guid.NewGuid():N}", Version = T0, ServerHash = "server-hash",
            IsCurator = curator,
        };
        _database.Context.Users.Add(user);
        await _database.Context.SaveChangesAsync();
        return user;
    }

    private async Task<Song> SeedSongAsync(string name = "Song", string? fileHash = null, string? externalSongId = null, params string[] artists)
    {
        var song = new Song
        {
            SongId = Guid.NewGuid().ToString(),
            Name = name,
            FileExtension = ".mp3",
            FileHash = fileHash ?? Guid.NewGuid().ToString(),
            ExternalSongId = externalSongId,
            Version = T0,
        };
        _database.Context.Songs.Add(song);
        foreach (var name_ in artists)
        {
            var artist = new Artist { ArtistId = Guid.NewGuid().ToString(), Name = name_ };
            _database.Context.Artists.Add(artist);
            _database.Context.SongArtists.Add(new SongArtist { SongArtistId = Guid.NewGuid().ToString(), SongId = song.SongId, ArtistId = artist.ArtistId });
        }
        await _database.Context.SaveChangesAsync();
        return song;
    }

    private async Task OwnAsync(User user, Song song, LikeStatus likeStatus = LikeStatus.Neutral)
    {
        _database.Context.UserSongs.Add(new UserSong
        {
            UserId = user.UserId, SongId = song.SongId, LastPlayed = DateTime.MinValue, Version = T0, LikeStatus = likeStatus,
        });
        await _database.Context.SaveChangesAsync();
    }

    /// <summary>A user who owns two copies of one recording.</summary>
    private async Task<(User Me, Song A, Song B)> SeedPairAsync()
    {
        var me = await SeedUserAsync();
        var a = await SeedSongAsync("A");
        var b = await SeedSongAsync("B");
        await OwnAsync(me, a);
        await OwnAsync(me, b);
        return (me, a, b);
    }

    private static SongDuplicateReportItem Item(string reportId, string songId, string? otherSongId, string kind = "duplicate",
        string? preferred = null, string? note = null)
        => new(reportId, songId, otherSongId, kind, preferred, note);

    private async Task<ReportSongDuplicatesResponse> ReportAsync(string userId, params SongDuplicateReportItem[] items)
    {
        await using var context = _database.CreateContext();
        var result = await new ReportSongDuplicatesHandler(context).Handle(new ReportSongDuplicatesCommand(userId, items), CancellationToken.None);
        Assert.True(result.IsOk, result.IsError ? result.GetError().Message : null);
        return result.Get();
    }

    private async Task<List<SongDuplicateReport>> StoredAsync()
    {
        await using var context = _database.CreateContext();
        return await context.SongDuplicateReports.AsNoTracking().OrderBy(r => r.CreatedAt).ThenBy(r => r.ReportId).ToListAsync();
    }

    private async Task<GetMySongDuplicateReportsResponse> MineAsync(string userId, DateTime afterDate, string? afterId = null)
    {
        await using var context = _database.CreateContext();
        var result = await new GetMySongDuplicateReportsHandler(context)
            .Handle(new GetMySongDuplicateReportsQuery(userId, afterDate, afterId), CancellationToken.None);
        Assert.True(result.IsOk);
        return result.Get();
    }

    private async Task<Result<WithdrawSongDuplicateReportResponse>> WithdrawAsync(string userId, string reportId)
    {
        await using var context = _database.CreateContext();
        return await new WithdrawSongDuplicateReportHandler(context).Handle(new WithdrawSongDuplicateReportCommand(userId, reportId), CancellationToken.None);
    }

    private async Task<ResolveSongDuplicateReportsResponse> ResolveAsync(string curatorId, string songId, string otherSongId, string resolution)
    {
        await using var context = _database.CreateContext();
        var result = await new ResolveSongDuplicateReportsHandler(context)
            .Handle(new ResolveSongDuplicateReportsCommand(curatorId, songId, otherSongId, resolution), CancellationToken.None);
        Assert.True(result.IsOk, result.IsError ? result.GetError().Message : null);
        return result.Get();
    }

    private async Task<GetSongDuplicateQueueResponse> QueueAsync()
    {
        await using var context = _database.CreateContext();
        var result = await new GetSongDuplicateQueueHandler(context).Handle(new GetSongDuplicateQueueQuery(), CancellationToken.None);
        Assert.True(result.IsOk, result.IsError ? result.GetError().Message : null);
        return result.Get();
    }

    [Fact]
    public async Task Report_NewPair_RecordsAPendingReportThatHidesTheSongReportedFrom()
    {
        var (me, a, b) = await SeedPairAsync();

        var response = await ReportAsync(me.UserId, Item("r1", a.SongId, b.SongId, note: "  same upload  "));

        var result = Assert.Single(response.Results);
        Assert.Equal(("r1", SongDuplicateReportOutcomes.Recorded, (string?)null), (result.ReportId, result.Outcome, result.Error));
        Assert.Equal("pending", result.Report!.Status);
        Assert.Equal("duplicate", result.Report.Kind);
        Assert.Equal(a.SongId, result.Report.HiddenSongId);
        Assert.Equal("same upload", result.Report.Note);
        Assert.Equal(me.UserId, result.Report.ReporterUserId);

        var stored = Assert.Single(await StoredAsync());
        Assert.Equal(SongDuplicateReportStatus.Pending, stored.Status);
        Assert.Null(stored.PreferredSongId);
    }

    [Fact]
    public async Task Report_PreferringTheSongReportedFrom_HidesTheOtherOne()
    {
        var (me, a, b) = await SeedPairAsync();

        var response = await ReportAsync(me.UserId,
            Item("r1", a.SongId, b.SongId, preferred: a.SongId),
            Item("r2", b.SongId, null, kind: "differentVersion"));

        Assert.Equal(b.SongId, response.Results[0].Report!.HiddenSongId);
        Assert.Equal(b.SongId, response.Results[1].Report!.HiddenSongId);
        Assert.Equal("differentVersion", response.Results[1].Report!.Kind);
        Assert.Null(response.Results[1].Report!.OtherSongId);
    }

    [Fact]
    public async Task Report_PerItemOutcomes_RejectEachBadItemAndRecordTheRest()
    {
        var (me, a, b) = await SeedPairAsync();
        var notMine = await SeedSongAsync("Not mine");

        var response = await ReportAsync(me.UserId,
            Item("ok", a.SongId, b.SongId),
            Item("unknown", a.SongId, "no-such-song"),
            Item("not-owned", a.SongId, notMine.SongId),
            Item("self", a.SongId, a.SongId),
            Item("bad-kind", a.SongId, b.SongId, kind: "sameish"),
            Item("needs-other", a.SongId, null, kind: "duplicate"),
            Item("bad-preferred", a.SongId, b.SongId, preferred: notMine.SongId),
            Item("", a.SongId, b.SongId));

        Assert.Equal(
            [
                ("ok", SongDuplicateReportOutcomes.Recorded, null),
                ("unknown", SongDuplicateReportOutcomes.Rejected, "Unknown song 'no-such-song'."),
                ("not-owned", SongDuplicateReportOutcomes.Rejected, $"Song '{notMine.SongId}' is not in your library."),
                ("self", SongDuplicateReportOutcomes.Rejected, "otherSongId must be a different song from songId."),
                ("bad-kind", SongDuplicateReportOutcomes.Rejected, "Unknown kind 'sameish'."),
                ("needs-other", SongDuplicateReportOutcomes.Rejected, "A duplicate report needs otherSongId."),
                ("bad-preferred", SongDuplicateReportOutcomes.Rejected, "preferredSongId must be one of the reported songs."),
                ("", SongDuplicateReportOutcomes.Rejected, "reportId is required."),
            ],
            response.Results.Select(r => (r.ReportId, r.Outcome, r.Error)).ToList<(string?, string, string?)>());
        Assert.Equal("ok", Assert.Single(await StoredAsync()).ReportId);
    }

    [Fact]
    public async Task Report_SamePairAgainUnderANewId_UpdatesTheFirstReport()
    {
        var (me, a, b) = await SeedPairAsync();
        await ReportAsync(me.UserId, Item("r1", a.SongId, b.SongId));

        // Reported from the other song this time, keeping it: same unordered pair.
        var response = await ReportAsync(me.UserId, Item("r2", b.SongId, a.SongId, preferred: b.SongId));

        var result = Assert.Single(response.Results);
        Assert.Equal(("r2", SongDuplicateReportOutcomes.Updated), (result.ReportId, result.Outcome));
        Assert.Equal("r1", result.Report!.ReportId);
        var stored = Assert.Single(await StoredAsync());
        Assert.Equal(("r1", b.SongId, a.SongId, a.SongId), (stored.ReportId, stored.SongId, stored.OtherSongId, stored.HiddenSongId));
    }

    [Fact]
    public async Task Report_Resend_ChangesNothing()
    {
        var (me, a, b) = await SeedPairAsync();
        var first = await ReportAsync(me.UserId, Item("r1", a.SongId, b.SongId));

        var again = await ReportAsync(me.UserId, Item("r1", a.SongId, b.SongId));

        Assert.Equal(SongDuplicateReportOutcomes.Recorded, Assert.Single(again.Results).Outcome);
        Assert.Equal(first.Results[0].Report!.Version, again.Results[0].Report!.Version);
        Assert.Single(await StoredAsync());
    }

    [Fact]
    public async Task Report_IdAlreadyUsedByAnotherUser_IsRejected()
    {
        var (me, a, b) = await SeedPairAsync();
        var (other, c, d) = await SeedPairAsync();
        await ReportAsync(other.UserId, Item("shared-id", c.SongId, d.SongId));

        var response = await ReportAsync(me.UserId, Item("shared-id", a.SongId, b.SongId));

        Assert.Equal(
            (SongDuplicateReportOutcomes.Rejected, "This reportId is already used by another report."),
            (response.Results[0].Outcome, response.Results[0].Error));
        Assert.Equal(other.UserId, Assert.Single(await StoredAsync()).ReporterUserId);
    }

    [Fact]
    public async Task Report_NeverChangesTheLikeStatus()
    {
        var me = await SeedUserAsync();
        var a = await SeedSongAsync("A");
        var b = await SeedSongAsync("B");
        await OwnAsync(me, a, LikeStatus.Like);
        await OwnAsync(me, b, LikeStatus.Love);

        await ReportAsync(me.UserId, Item("r1", a.SongId, b.SongId));

        await using var context = _database.CreateContext();
        var statuses = await context.UserSongs.Where(us => us.UserId == me.UserId).OrderBy(us => us.SongId == a.SongId ? 0 : 1)
            .Select(us => us.LikeStatus).ToListAsync();
        Assert.Equal([LikeStatus.Like, LikeStatus.Love], statuses);
    }

    [Fact]
    public async Task Report_AfterACuratorKeptBoth_ReopensThePairForThatUser()
    {
        var (me, a, b) = await SeedPairAsync();
        var curator = await SeedUserAsync(curator: true);
        await ReportAsync(me.UserId, Item("r1", a.SongId, b.SongId));
        await ResolveAsync(curator.UserId, a.SongId, b.SongId, "keptBoth");

        var response = await ReportAsync(me.UserId, Item("r2", a.SongId, b.SongId, note: "still the same"));

        Assert.Equal(SongDuplicateReportOutcomes.Updated, response.Results[0].Outcome);
        var stored = Assert.Single(await StoredAsync());
        Assert.Equal((SongDuplicateReportStatus.Pending, (DateTime?)null, (string?)null), (stored.Status, stored.ResolvedAt, stored.ResolvedByUserId));
    }

    [Fact]
    public async Task Mine_ReturnsOnlyTheCallersReports_AndPagesThroughATieOnVersion()
    {
        var me = await SeedUserAsync();
        var other = await SeedUserAsync();
        _database.Context.SongDuplicateReports.AddRange(Enumerable.Range(0, GetMySongDuplicateReportsHandler.PageSize + 3)
            .Select(i => new SongDuplicateReport
            {
                ReportId = $"r{i:D4}", ReporterUserId = me.UserId, SongId = $"s{i}", HiddenSongId = $"s{i}", PairKey = $"s{i}",
                Kind = SongDuplicateReportKind.DifferentVersion, CreatedAt = T0, Version = T0,
            }));
        _database.Context.SongDuplicateReports.Add(new SongDuplicateReport
        {
            ReportId = "theirs", ReporterUserId = other.UserId, SongId = "x", HiddenSongId = "x", PairKey = "x",
            Kind = SongDuplicateReportKind.DifferentVersion, CreatedAt = T0, Version = T0,
        });
        await _database.Context.SaveChangesAsync();

        var first = await MineAsync(me.UserId, DateTime.MinValue);
        Assert.False(first.EndOfList);
        Assert.Equal(GetMySongDuplicateReportsHandler.PageSize, first.Reports.Count);

        var last = first.Reports[^1];
        var second = await MineAsync(me.UserId, last.Version, last.ReportId);
        Assert.True(second.EndOfList);
        Assert.Equal(["r0500", "r0501", "r0502"], second.Reports.Select(r => r.ReportId));
        Assert.DoesNotContain(first.Reports.Concat(second.Reports), r => r.ReportId == "theirs");
    }

    [Fact]
    public async Task Withdraw_MarksTheReportWithdrawn_KeepsTheRow_AndShowsInTheFeed()
    {
        var (me, a, b) = await SeedPairAsync();
        var recorded = (await ReportAsync(me.UserId, Item("r1", a.SongId, b.SongId))).Results[0].Report!;

        var result = await WithdrawAsync(me.UserId, "r1");

        Assert.True(result.IsOk);
        Assert.Equal("withdrawn", result.Get().Report.Status);
        Assert.Equal(me.UserId, result.Get().Report.ResolvedByUserId);
        var stored = Assert.Single(await StoredAsync());
        Assert.Equal(SongDuplicateReportStatus.Withdrawn, stored.Status);
        Assert.NotNull(stored.ResolvedAt);

        var feed = await MineAsync(me.UserId, recorded.Version, recorded.ReportId);
        Assert.Equal("withdrawn", Assert.Single(feed.Reports).Status);
    }

    [Fact]
    public async Task Withdraw_Resend_IsOkAndChangesNothing()
    {
        var (me, a, b) = await SeedPairAsync();
        await ReportAsync(me.UserId, Item("r1", a.SongId, b.SongId));
        var first = (await WithdrawAsync(me.UserId, "r1")).Get().Report;

        var again = await WithdrawAsync(me.UserId, "r1");

        Assert.True(again.IsOk);
        Assert.Equal(first.Version, again.Get().Report.Version);
    }

    [Fact]
    public async Task Withdraw_SomeoneElsesReportOrAMissingOne_IsNotFoundAndChangesNothing()
    {
        var (me, a, b) = await SeedPairAsync();
        var stranger = await SeedUserAsync();
        await ReportAsync(me.UserId, Item("r1", a.SongId, b.SongId));

        var theirs = await WithdrawAsync(stranger.UserId, "r1");
        var missing = await WithdrawAsync(me.UserId, "nope");

        Assert.Equal(ErrorKind.NotFound, ResultErrors.KindOf(theirs.GetError()));
        Assert.Equal(ErrorKind.NotFound, ResultErrors.KindOf(missing.GetError()));
        Assert.Equal(SongDuplicateReportStatus.Pending, Assert.Single(await StoredAsync()).Status);
    }

    [Fact]
    public async Task Resolve_ClosesEveryOpenReportOnThePair_AndLeavesOthersAlone()
    {
        var curator = await SeedUserAsync(curator: true);
        var a = await SeedSongAsync("A");
        var b = await SeedSongAsync("B");
        var c = await SeedSongAsync("C");
        var users = new List<User>();
        for (var i = 0; i < 3; i++)
        {
            var user = await SeedUserAsync();
            await OwnAsync(user, a);
            await OwnAsync(user, b);
            await OwnAsync(user, c);
            users.Add(user);
        }
        await ReportAsync(users[0].UserId, Item("u0", a.SongId, b.SongId));
        await ReportAsync(users[1].UserId, Item("u1", b.SongId, a.SongId, preferred: b.SongId));
        await ReportAsync(users[2].UserId, Item("u2", a.SongId, b.SongId), Item("u2-other", a.SongId, c.SongId));
        await WithdrawAsync(users[2].UserId, "u2");

        // Named in the other order: still the same pair.
        var response = await ResolveAsync(curator.UserId, b.SongId, a.SongId, "notDuplicate");

        Assert.Equal(["u0", "u1"], response.Reports.Select(r => r.ReportId).Order());
        Assert.All(response.Reports, r => Assert.Equal(("notDuplicate", curator.UserId), (r.Status, r.ResolvedByUserId)));
        Assert.Equal(2, response.Reports.Select(r => r.Version).Distinct().Count());

        var stored = (await StoredAsync()).ToDictionary(r => r.ReportId);
        Assert.Equal(SongDuplicateReportStatus.NotDuplicate, stored["u0"].Status);
        Assert.Equal(SongDuplicateReportStatus.NotDuplicate, stored["u1"].Status);
        Assert.Equal(SongDuplicateReportStatus.Withdrawn, stored["u2"].Status);
        Assert.Equal(SongDuplicateReportStatus.Pending, stored["u2-other"].Status);
        // The verdict doesn't lift the hide.
        Assert.Equal(a.SongId, stored["u1"].HiddenSongId);

        var again = await ResolveAsync(curator.UserId, a.SongId, b.SongId, "keptBoth");
        Assert.Empty(again.Reports);
    }

    [Fact]
    public async Task Queue_GroupsPendingPairReports_WithWhatTheCuratorNeeds()
    {
        var hash = Guid.NewGuid().ToString();
        var a = await SeedSongAsync("Everybody Wants You", hash, "yt-a", "Billy Squier");
        var b = await SeedSongAsync("Everybody Wants You (Remastered)", hash, "yt-b", "Billy Squier", "Burnard");
        var lone = await SeedSongAsync("Live version");
        var gone = await SeedSongAsync("Gone");
        var first = await SeedUserAsync();
        var second = await SeedUserAsync();
        await OwnAsync(first, a, LikeStatus.Like);
        await OwnAsync(first, b);
        await OwnAsync(first, lone);
        await OwnAsync(first, gone);
        await OwnAsync(second, a, LikeStatus.Love);
        await OwnAsync(second, b, LikeStatus.Dislike);
        _database.Context.Listens.Add(new Listen
        {
            ListenId = Guid.NewGuid().ToString(), UserId = first.UserId, SongId = a.SongId, StartedAt = T0, EndedAt = T0.AddMinutes(4),
            SecondsListened = 240, SongDurationSeconds = 241.5, RecordedAt = T0,
        });
        await _database.Context.SaveChangesAsync();

        await ReportAsync(first.UserId,
            Item("f1", a.SongId, b.SongId, note: "same song"),
            Item("f-lone", lone.SongId, null, kind: "differentVersion"),
            Item("f-gone", a.SongId, gone.SongId));
        await ReportAsync(second.UserId, Item("s1", b.SongId, a.SongId, kind: "differentVersion", preferred: a.SongId));
        _database.Context.Songs.Remove(await _database.Context.Songs.SingleAsync(s => s.SongId == gone.SongId));
        await _database.Context.SaveChangesAsync();

        var queue = await QueueAsync();

        Assert.Equal(0, queue.Remaining);
        var pair = Assert.Single(queue.Pairs);
        var (firstId, secondId) = string.CompareOrdinal(a.SongId, b.SongId) <= 0 ? (a.SongId, b.SongId) : (b.SongId, a.SongId);
        Assert.Equal((firstId, secondId), (pair.FirstSong.SongId, pair.SecondSong.SongId));
        Assert.True(pair.FileHashMatch);
        Assert.Equal(2, pair.ReportCount);
        Assert.Equal(["f1", "s1"], pair.Reports.Select(r => r.ReportId).Order());

        var songA = pair.FirstSong.SongId == a.SongId ? pair.FirstSong : pair.SecondSong;
        var songB = pair.FirstSong.SongId == a.SongId ? pair.SecondSong : pair.FirstSong;
        Assert.Equal(("Everybody Wants You", "yt-a", 241.5, 2), (songA.Name, songA.ExternalSongId, songA.DurationSeconds, songA.LikeCount));
        Assert.Equal(["Billy Squier"], songA.Artists);
        Assert.Equal((null, 0), (songB.DurationSeconds, songB.LikeCount));
        Assert.Equal(["Billy Squier", "Burnard"], songB.Artists.Order());
        var s1 = pair.Reports.Single(r => r.ReportId == "s1");
        Assert.Equal(("differentVersion", a.SongId, second.UserId), (s1.Kind, s1.PreferredSongId, s1.ReporterUserId));
    }

    [Fact]
    public async Task Queue_LeavesOutResolvedPairs()
    {
        var (me, a, b) = await SeedPairAsync();
        var curator = await SeedUserAsync(curator: true);
        await ReportAsync(me.UserId, Item("r1", a.SongId, b.SongId));
        await ResolveAsync(curator.UserId, a.SongId, b.SongId, "keptBoth");

        Assert.Empty((await QueueAsync()).Pairs);
    }
}
