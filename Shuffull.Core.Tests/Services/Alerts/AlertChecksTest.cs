using Nut.Results;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Services.Alerts;
using Shuffull.Core.Services.Alerts.Checks;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Services.Alerts;

/// <summary>Each check's query against a real database, and the site's signal rules.</summary>
public class AlertChecksTest : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);

    private readonly DatabaseFixture _database = new();

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<IReadOnlyList<AlertFinding>> EvaluateAsync(IAlertCheck check)
    {
        var result = await check.EvaluateAsync(Now, CancellationToken.None);
        Assert.True(result.IsOk);
        return result.Get();
    }

    private async Task<Song> SeedSongAsync(bool exploratory = false, string? tagModel = null, int tagAttempts = 0,
        bool locked = false, string name = "Song")
    {
        var song = new Song
        {
            SongId = Guid.NewGuid().ToString(),
            Name = name,
            FileExtension = ".mp3",
            FileHash = Guid.NewGuid().ToString(),
            Exploratory = exploratory,
            TagModel = tagModel,
            TagAttempts = tagAttempts,
            MetadataLocked = locked,
            Version = Now,
        };
        _database.Context.Songs.Add(song);
        await _database.Context.SaveChangesAsync();
        return song;
    }

    private async Task SeedSignalAsync(string key, Action<HealthSignal> record)
    {
        var signal = HealthSignal.Create(key);
        record(signal);
        _database.Context.HealthSignals.Add(signal);
        await _database.Context.SaveChangesAsync();
    }

    // ── Signal rules ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FunnelProbe_AlertsOnTheThirdMissInARow()
    {
        await SeedSignalAsync(ShuffullSignalRules.FunnelProbe, s =>
        {
            s.RecordFailure("connection refused", Now.AddMinutes(-10));
            s.RecordFailure("connection refused", Now.AddMinutes(-5));
        });
        Assert.Empty(await EvaluateAsync(new SignalAlertCheck(_database.CreateContext())));

        var context = _database.CreateContext();
        context.HealthSignals.Single().RecordFailure("connection refused", Now);
        await context.SaveChangesAsync();

        var finding = Assert.Single(await EvaluateAsync(new SignalAlertCheck(_database.CreateContext())));
        Assert.Equal("signal:failing:probe:funnel", finding.Key);
        Assert.Equal("The funnel is down", finding.Subject);
        Assert.Contains("connection refused", finding.Body);
    }

    [Fact]
    public async Task FunnelPoll_AlertsOnlyOnceQuietForAnHour()
    {
        await SeedSignalAsync(ShuffullSignalRules.PendingTagsPoll, s => s.RecordSuccess(Now.AddMinutes(-59)));
        await SeedSignalAsync(ShuffullSignalRules.YoutubeRatingsPoll, s => s.RecordSuccess(Now.AddMinutes(-61)));

        var finding = Assert.Single(await EvaluateAsync(new SignalAlertCheck(_database.CreateContext())));
        Assert.Equal("signal:stale:funnel-poll:youtube-ratings", finding.Key);
        Assert.Equal("The funnel stopped syncing likes to YouTube", finding.Subject);
    }

    [Fact]
    public async Task KeepRefused_FiresForADay_ThenClears()
    {
        await SeedSignalAsync(ShuffullSignalRules.KeepRefused, s =>
        {
            s.RecordFailure("User u1: 1 of 1 song(s) refused", Now.AddDays(-3));
            s.RecordFailure("User u2: 2 of 5 song(s) refused", Now.AddHours(-23));
        });

        var finding = Assert.Single(await EvaluateAsync(new SignalAlertCheck(_database.CreateContext())));
        Assert.Equal("signal:failing:keep-refused", finding.Key);
        Assert.Equal(1, finding.Level);
        Assert.Contains("User u2: 2 of 5", finding.Body);
        Assert.Contains("2 in total", finding.Body);

        var later = await new SignalAlertCheck(_database.CreateContext()).EvaluateAsync(Now.AddHours(2), CancellationToken.None);
        Assert.Empty(later.Get());
    }

    [Fact]
    public async Task MediaSweep_AlertsAfterThreeHoursOfFailedPasses()
    {
        await SeedSignalAsync(ShuffullSignalRules.MediaSweep, s =>
        {
            s.RecordFailure("disk", Now.AddHours(-2));
            s.RecordFailure("disk", Now.AddHours(-1));
            s.RecordFailure("disk", Now);
        });
        Assert.Empty(await EvaluateAsync(new SignalAlertCheck(_database.CreateContext())));

        var later = await new SignalAlertCheck(_database.CreateContext()).EvaluateAsync(Now.AddHours(1), CancellationToken.None);
        Assert.Equal("signal:failing:media-sweep", Assert.Single(later.Get()).Key);
    }

    [Fact]
    public async Task UnknownSignal_NeverAlerts()
    {
        await SeedSignalAsync("something-new", s => s.RecordFailure("boom", Now.AddDays(-5)));
        Assert.Empty(await EvaluateAsync(new SignalAlertCheck(_database.CreateContext())));
    }

    // ── Like-sync backlog ────────────────────────────────────────────────────────────────────────────────

    private async Task SeedRatingAsync(YoutubeRatingStatus status, DateTime updatedAt, DateTime? createdAt = null)
    {
        var song = await SeedSongAsync();
        _database.Context.YoutubeRatingRequests.Add(new YoutubeRatingRequest
        {
            YoutubeRatingRequestId = Guid.NewGuid().ToString(),
            SongId = song.SongId,
            VideoId = Guid.NewGuid().ToString("N")[..11],
            Rating = YoutubeRating.Like,
            Status = status,
            CreatedAt = createdAt ?? updatedAt,
            UpdatedAt = updatedAt,
        });
        await _database.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task LikeSync_DayOldPendingRows_Alert()
    {
        await SeedRatingAsync(YoutubeRatingStatus.Pending, Now.AddHours(-25));
        await SeedRatingAsync(YoutubeRatingStatus.Pending, Now.AddHours(-30));
        await SeedRatingAsync(YoutubeRatingStatus.Pending, Now.AddHours(-1));
        await SeedRatingAsync(YoutubeRatingStatus.Completed, Now.AddDays(-10));

        var finding = Assert.Single(await EvaluateAsync(new LikeSyncBacklogCheck(_database.CreateContext())));
        Assert.Equal("like-sync:backlog", finding.Key);
        Assert.StartsWith("2 like(s)", finding.Subject);
    }

    [Fact]
    public async Task LikeSync_ReopenedRow_IsAgedFromItsReopening()
    {
        // A video rated long ago and re-rated an hour ago: its old CreatedAt must not make it look stuck.
        await SeedRatingAsync(YoutubeRatingStatus.Pending, updatedAt: Now.AddHours(-1), createdAt: Now.AddDays(-60));
        Assert.Empty(await EvaluateAsync(new LikeSyncBacklogCheck(_database.CreateContext())));
    }

    // ── Untagged kept songs ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UntaggedKeptSongs_OnlyCountsKeptUnlockedSongsOfferedOften()
    {
        var stuck = await SeedSongAsync(tagAttempts: 6, name: "Stuck");
        await SeedSongAsync(tagAttempts: 5);                            // not offered often enough yet
        await SeedSongAsync(tagAttempts: 9, tagModel: "gpt-5.4-nano"); // tagged
        await SeedSongAsync(tagAttempts: 9, exploratory: true);        // still an audition
        await SeedSongAsync(tagAttempts: 9, locked: true);             // curator-locked

        var finding = Assert.Single(await EvaluateAsync(new UntaggedKeptSongsCheck(_database.CreateContext())));
        Assert.Equal("untagged:kept-songs", finding.Key);
        Assert.StartsWith("1 kept song(s)", finding.Subject);
        Assert.Contains(stuck.SongId, finding.Body);
    }

    [Fact]
    public async Task UntaggedKeptSongs_NoneLeft_NoFinding()
    {
        await SeedSongAsync(tagAttempts: 2);
        Assert.Empty(await EvaluateAsync(new UntaggedKeptSongsCheck(_database.CreateContext())));
    }

    // ── Abandoned media ──────────────────────────────────────────────────────────────────────────────────

    private async Task SeedTombstoneAsync(DateTime? sweptAt, bool deleted, int failures)
    {
        _database.Context.SongTombstones.Add(new SongTombstone
        {
            SongId = Guid.NewGuid().ToString(),
            Name = "Gone",
            FileHash = Guid.NewGuid().ToString(),
            FileExtension = ".mp3",
            DeletedByUserId = "user",
            DeletedAt = (sweptAt ?? Now) - TimeSpan.FromDays(8),
            MediaSweptAt = sweptAt,
            MediaDeleted = deleted,
            MediaSweepFailures = failures,
        });
        await _database.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task AbandonedMedia_OnlyRecentGiveUps()
    {
        await SeedTombstoneAsync(Now.AddDays(-2), deleted: false, failures: SongTombstone.MaxMediaDeleteAttempts);
        await SeedTombstoneAsync(Now.AddDays(-40), deleted: false, failures: SongTombstone.MaxMediaDeleteAttempts); // old
        await SeedTombstoneAsync(Now.AddDays(-2), deleted: false, failures: 0);  // kept: another song holds the hash
        await SeedTombstoneAsync(Now.AddDays(-2), deleted: true, failures: 3);   // deleted on a retry
        await SeedTombstoneAsync(null, deleted: false, failures: 4);             // still being retried

        var finding = Assert.Single(await EvaluateAsync(new AbandonedMediaCheck(_database.CreateContext())));
        Assert.Equal("media:abandoned", finding.Key);
        Assert.Contains("gave up on 1 ", finding.Subject);
    }
}
