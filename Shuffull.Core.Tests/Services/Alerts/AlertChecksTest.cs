using Nut.Results;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Alerts;
using Shuffull.Core.Alerts.Checks;
using Shuffull.Core.Services.Alerts;
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

    /// <summary>The API has been up long enough that the startup grace plays no part.</summary>
    private static readonly DateTime LongAgo = Now.AddDays(-30);

    private SignalAlertCheck Signals(DateTime? startedUtc = null) => new(_database.CreateContext(), startedUtc ?? LongAgo);

    /// <summary>Both polls seen just now, so tests about other signals aren't joined by "never polled" findings.</summary>
    private async Task SeedFreshPollsAsync(DateTime? at = null)
    {
        await SeedSignalAsync(ShuffullSignalRules.PendingTagsPoll, s => s.RecordSuccess(at ?? Now));
        await SeedSignalAsync(ShuffullSignalRules.YoutubeRatingsPoll, s => s.RecordSuccess(at ?? Now));
    }

    [Fact]
    public async Task FunnelProbe_AlertsOnTheThirdMissInARow()
    {
        await SeedFreshPollsAsync();
        await SeedSignalAsync(ShuffullSignalRules.FunnelProbe, s =>
        {
            s.RecordFailure("connection refused", Now.AddMinutes(-10));
            s.RecordFailure("connection refused", Now.AddMinutes(-5));
        });
        Assert.Empty(await EvaluateAsync(Signals()));

        var context = _database.CreateContext();
        context.HealthSignals.Single(s => s.Key == ShuffullSignalRules.FunnelProbe).RecordFailure("connection refused", Now);
        await context.SaveChangesAsync();

        var finding = Assert.Single(await EvaluateAsync(Signals()));
        Assert.Equal("signal:failing:probe:funnel", finding.Key);
        Assert.Equal("The funnel is down", finding.Subject);
        Assert.Contains("connection refused", finding.Body);
    }

    [Fact]
    public async Task FunnelPoll_AlertsOnlyOnceQuietForAnHour()
    {
        await SeedSignalAsync(ShuffullSignalRules.PendingTagsPoll, s => s.RecordSuccess(Now.AddMinutes(-59)));
        await SeedSignalAsync(ShuffullSignalRules.YoutubeRatingsPoll, s => s.RecordSuccess(Now.AddMinutes(-61)));

        var finding = Assert.Single(await EvaluateAsync(Signals()));
        Assert.Equal("signal:stale:funnel-poll:youtube-ratings", finding.Key);
        Assert.Equal("The funnel stopped syncing likes to YouTube", finding.Subject);
    }

    [Fact]
    public async Task FunnelPoll_SilenceDuringAnApiOutage_DoesNotCount()
    {
        // The polls last arrived 5 h ago because the API itself was down; it came back 10 minutes ago.
        await SeedSignalAsync(ShuffullSignalRules.PendingTagsPoll, s => s.RecordSuccess(Now.AddHours(-5)));
        await SeedSignalAsync(ShuffullSignalRules.YoutubeRatingsPoll, s => s.RecordSuccess(Now.AddHours(-5)));

        Assert.Empty(await EvaluateAsync(Signals(startedUtc: Now.AddMinutes(-10))));

        // An hour after the restart with still no poll, the funnel really is quiet.
        var later = await Signals(startedUtc: Now.AddMinutes(-10)).EvaluateAsync(Now.AddMinutes(51), CancellationToken.None);
        Assert.Equal(2, later.Get().Count);
    }

    [Fact]
    public async Task FunnelPoll_NeverRecorded_AlertsAnHourAfterStart()
    {
        Assert.Empty(await EvaluateAsync(Signals(startedUtc: Now.AddMinutes(-59))));

        var findings = await EvaluateAsync(Signals(startedUtc: Now.AddMinutes(-60)));
        Assert.Equal(
            ["signal:stale:funnel-poll:pending-tags", "signal:stale:funnel-poll:youtube-ratings"],
            findings.Select(f => f.Key).Order().ToList());
        Assert.Contains("never before that either", findings[0].Body);
    }

    [Fact]
    public async Task KeepRefused_EachSongIsItsOwnAlert_ForADay()
    {
        await SeedFreshPollsAsync();
        await SeedSignalAsync(ShuffullSignalRules.KeepRefusedKey("u1", "song-a"), s =>
        {
            s.RecordFailure("User u1, song song-a: Song is not in your library.", Now.AddHours(-5));
            s.RecordFailure("User u1, song song-a: Song is not in your library.", Now.AddHours(-1));
        });
        await SeedSignalAsync(ShuffullSignalRules.KeepRefusedKey("u2", "song-b"),
            s => s.RecordFailure("User u2, song song-b: Song is not in your library.", Now.AddHours(-23)));
        await SeedSignalAsync(ShuffullSignalRules.KeepRefusedKey("u2", "song-c"),
            s => s.RecordFailure("User u2, song song-c: Song is not in your library.", Now.AddDays(-2)));

        var findings = (await EvaluateAsync(Signals())).OrderBy(f => f.Key).ToList();
        Assert.Equal(["signal:failing:keep-refused:u1:song-a", "signal:failing:keep-refused:u2:song-b"],
            findings.Select(f => f.Key).ToList());
        Assert.Equal("A Keep was refused (song song-a)", findings[0].Subject);
        Assert.Equal(1, findings[0].Level);
        Assert.Contains("2 time(s) in total", findings[0].Body);
    }

    [Fact]
    public async Task FailingSignal_NothingRecordsForAWeek_Retires()
    {
        await SeedFreshPollsAsync();
        await SeedSignalAsync(ShuffullSignalRules.FunnelProbe, s =>
        {
            for (var i = 0; i < 3; i++)
            {
                s.RecordFailure("connection refused", Now.AddDays(-8));
            }
        });

        Assert.Empty(await EvaluateAsync(Signals()));
    }

    [Fact]
    public async Task MediaSweep_AlertsAfterThreeHoursOfFailedPasses()
    {
        await SeedFreshPollsAsync(at: Now.AddHours(1));
        await SeedSignalAsync(ShuffullSignalRules.MediaSweep, s =>
        {
            s.RecordFailure("disk", Now.AddHours(-2));
            s.RecordFailure("disk", Now.AddHours(-1));
            s.RecordFailure("disk", Now);
        });
        Assert.Empty(await EvaluateAsync(Signals()));

        var later = await Signals().EvaluateAsync(Now.AddHours(1), CancellationToken.None);
        Assert.Equal("signal:failing:media-sweep", Assert.Single(later.Get()).Key);
    }

    [Fact]
    public async Task UnknownSignal_NeverAlerts()
    {
        await SeedFreshPollsAsync();
        await SeedSignalAsync("something-new", s => s.RecordFailure("boom", Now.AddDays(-5)));
        Assert.Empty(await EvaluateAsync(Signals()));
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
    public void UntaggedKeptSongs_Threshold_SpansTwoDaysOfHandOuts()
    {
        // 5 + 10 + 20 + 40 min, then an hour per hand-out: the 52nd comes at least 48 h after the first.
        Assert.Equal(52, UntaggedKeptSongsCheck.MinAttempts);
        var elapsed = Enumerable.Range(1, UntaggedKeptSongsCheck.MinAttempts - 1)
            .Aggregate(TimeSpan.Zero, (sum, n) => sum + Shuffull.Core.Features.Songs.GetPendingTagSongs.GetPendingTagSongsHandler.RetryDelay(n));
        Assert.True(elapsed >= UntaggedKeptSongsCheck.MinTimeOffered);
    }

    [Fact]
    public async Task UntaggedKeptSongs_OnlyCountsKeptUnlockedSongsOfferedForDays()
    {
        var many = UntaggedKeptSongsCheck.MinAttempts;
        var stuck = await SeedSongAsync(tagAttempts: many, name: "Stuck");
        await SeedSongAsync(tagAttempts: many - 1);                       // a day and a half: maybe still in a batch
        await SeedSongAsync(tagAttempts: many, tagModel: "gpt-5.4-nano"); // tagged
        await SeedSongAsync(tagAttempts: many, exploratory: true);        // still an audition
        await SeedSongAsync(tagAttempts: many, locked: true);             // curator-locked

        var finding = Assert.Single(await EvaluateAsync(new UntaggedKeptSongsCheck(_database.CreateContext())));
        Assert.Equal("untagged:kept-songs", finding.Key);
        Assert.StartsWith("1 kept song(s)", finding.Subject);
        Assert.Contains(stuck.SongId, finding.Body);
    }

    [Fact]
    public async Task UntaggedKeptSongs_NoneLeft_NoFinding()
    {
        await SeedSongAsync(tagAttempts: 6);
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

    // ── Song request backlog ─────────────────────────────────────────────────────────────────────────────

    private async Task SeedSongRequestAsync(SongRequestStatus status, DateTime updatedAt, DateTime? createdAt = null)
    {
        _database.Context.SongRequests.Add(new SongRequest
        {
            SongRequestId = Guid.NewGuid().ToString(),
            UserId = "user",
            ExternalSongId = Guid.NewGuid().ToString("N")[..11],
            Title = "Everybody Wants You",
            TargetPlaylistName = "Requests · week of 2026-10-26",
            Status = status,
            CreatedAt = createdAt ?? updatedAt,
            UpdatedAt = updatedAt,
        });
        await _database.Context.SaveChangesAsync();
    }

    private SongRequestBacklogCheck SongRequests(DateTime? startedUtc = null) => new(_database.CreateContext(), startedUtc ?? LongAgo);

    [Fact]
    public async Task SongRequests_PendingAndNeverPolled_Alerts()
    {
        await SeedSongRequestAsync(SongRequestStatus.Pending, Now.AddMinutes(-5));
        await SeedSongRequestAsync(SongRequestStatus.Pending, Now.AddMinutes(-1));

        var finding = Assert.Single(await EvaluateAsync(SongRequests()));
        Assert.Equal("song-requests:not-polled", finding.Key);
        Assert.Contains("2 song request(s) are Pending", finding.Body);
        Assert.Contains("never polled", finding.Body);
    }

    [Fact]
    public async Task SongRequests_PendingAndPollSilentPastTheWindow_Alerts()
    {
        await SeedSignalAsync(ShuffullSignalRules.SongRequestsPoll, s => s.RecordSuccess(Now.AddMinutes(-31)));
        await SeedSongRequestAsync(SongRequestStatus.Pending, Now.AddMinutes(-5));

        Assert.Equal("song-requests:not-polled", Assert.Single(await EvaluateAsync(SongRequests())).Key);
        // Configurable: a longer window is still quiet.
        Assert.Empty(await EvaluateAsync(new SongRequestBacklogCheck(_database.CreateContext(), LongAgo,
            pollSilentAfter: TimeSpan.FromHours(1))));
    }

    [Fact]
    public async Task SongRequests_RecentPoll_IsQuiet()
    {
        await SeedSignalAsync(ShuffullSignalRules.SongRequestsPoll, s => s.RecordSuccess(Now.AddMinutes(-1)));
        await SeedSongRequestAsync(SongRequestStatus.Pending, Now.AddMinutes(-10));

        Assert.Empty(await EvaluateAsync(SongRequests()));
    }

    [Fact]
    public async Task SongRequests_NothingPending_IsQuietEvenWithNoPoller()
    {
        // Prod before the funnel polls: no signal row at all. Settled requests and fresh claims say nothing about it.
        await SeedSongRequestAsync(SongRequestStatus.Accepted, Now.AddMinutes(-5));
        await SeedSongRequestAsync(SongRequestStatus.Retrying, Now.AddMinutes(-1));
        await SeedSongRequestAsync(SongRequestStatus.Failed, Now.AddDays(-3));
        await SeedSongRequestAsync(SongRequestStatus.Completed, Now.AddDays(-3));
        await SeedSongRequestAsync(SongRequestStatus.Cancelled, Now.AddDays(-3));

        Assert.Empty(await EvaluateAsync(SongRequests()));
    }

    [Fact]
    public async Task SongRequests_SilenceDuringAnApiOutage_DoesNotCount()
    {
        await SeedSignalAsync(ShuffullSignalRules.SongRequestsPoll, s => s.RecordSuccess(Now.AddDays(-2)));
        await SeedSongRequestAsync(SongRequestStatus.Pending, Now.AddMinutes(-5));

        Assert.Empty(await EvaluateAsync(SongRequests(startedUtc: Now.AddMinutes(-10))));
        Assert.Single(await EvaluateAsync(SongRequests(startedUtc: Now.AddMinutes(-30))));
    }

    [Fact]
    public async Task SongRequests_OldPendingWhileThePollerRuns_AlertsAsStuck()
    {
        await SeedSignalAsync(ShuffullSignalRules.SongRequestsPoll, s => s.RecordSuccess(Now.AddMinutes(-1)));
        await SeedSongRequestAsync(SongRequestStatus.Pending, Now.AddHours(-3));
        await SeedSongRequestAsync(SongRequestStatus.Pending, Now.AddMinutes(-5));

        var finding = Assert.Single(await EvaluateAsync(SongRequests()));
        Assert.Equal("song-requests:stuck", finding.Key);
        Assert.Contains("2 song request(s) are Pending", finding.Body);
    }

    [Fact]
    public async Task SongRequests_RetriedRequest_IsAgedFromItsRetry()
    {
        // A Failed request the user retried goes back to Pending on its old row: its CreatedAt doesn't make it stuck.
        await SeedSignalAsync(ShuffullSignalRules.SongRequestsPoll, s => s.RecordSuccess(Now.AddMinutes(-1)));
        await SeedSongRequestAsync(SongRequestStatus.Pending, updatedAt: Now.AddMinutes(-10), createdAt: Now.AddDays(-5));

        Assert.Empty(await EvaluateAsync(SongRequests()));
    }
    [Fact]
    public async Task SongRequests_ClaimedRequestsWithNoProgress_Alert()
    {
        await SeedSignalAsync(ShuffullSignalRules.SongRequestsPoll, s => s.RecordSuccess(Now.AddMinutes(-1)));
        await SeedSongRequestAsync(SongRequestStatus.Accepted, Now.AddHours(-7));
        await SeedSongRequestAsync(SongRequestStatus.Downloading, Now.AddDays(-2));
        await SeedSongRequestAsync(SongRequestStatus.Retrying, Now.AddHours(-6));
        await SeedSongRequestAsync(SongRequestStatus.Downloading, Now.AddHours(-5)); // inside the window

        var finding = Assert.Single(await EvaluateAsync(SongRequests()));
        Assert.Equal("song-requests:no-progress", finding.Key);
        Assert.Equal(2, finding.Level);
        Assert.Equal(ShuffullSignalRules.Daily, finding.RemindEvery);
        Assert.Contains("3 song request(s)", finding.Subject);
        Assert.Contains("(1 Accepted, 1 Downloading, 1 Retrying)", finding.Body);
        Assert.Contains($"since {Now.AddDays(-2):yyyy-MM-dd HH:mm} UTC", finding.Body);
        Assert.Contains("last polled", finding.Body);
    }

    [Fact]
    public async Task SongRequests_ClaimedRequestsInsideTheWindow_AreQuiet()
    {
        await SeedSignalAsync(ShuffullSignalRules.SongRequestsPoll, s => s.RecordSuccess(Now.AddMinutes(-1)));
        await SeedSongRequestAsync(SongRequestStatus.Downloading, Now.AddHours(-5));
        // Settled ones are never watched, however old.
        await SeedSongRequestAsync(SongRequestStatus.Failed, Now.AddDays(-3));
        await SeedSongRequestAsync(SongRequestStatus.Completed, Now.AddDays(-3));
        await SeedSongRequestAsync(SongRequestStatus.Cancelled, Now.AddDays(-3));

        Assert.Empty(await EvaluateAsync(SongRequests()));
    }

    [Fact]
    public async Task SongRequests_NoProgressWindow_IsConfigurable()
    {
        await SeedSignalAsync(ShuffullSignalRules.SongRequestsPoll, s => s.RecordSuccess(Now.AddMinutes(-1)));
        await SeedSongRequestAsync(SongRequestStatus.Accepted, Now.AddHours(-2));

        Assert.Empty(await EvaluateAsync(SongRequests()));
        var finding = Assert.Single(await EvaluateAsync(new SongRequestBacklogCheck(_database.CreateContext(), LongAgo,
            noProgressAfter: TimeSpan.FromHours(1))));
        Assert.Equal("song-requests:no-progress", finding.Key);
        Assert.Contains("in 1 h", finding.Body);
    }

    [Fact]
    public async Task SongRequests_NoProgressDuringAnApiOutage_DoesNotCount()
    {
        // No ack could land while the API was down: the window runs from its start, not from the last ack.
        await SeedSignalAsync(ShuffullSignalRules.SongRequestsPoll, s => s.RecordSuccess(Now.AddMinutes(-1)));
        await SeedSongRequestAsync(SongRequestStatus.Downloading, Now.AddDays(-2));

        Assert.Empty(await EvaluateAsync(SongRequests(startedUtc: Now.AddHours(-5))));
        Assert.Single(await EvaluateAsync(SongRequests(startedUtc: Now.AddHours(-6))));
    }

    [Fact]
    public async Task SongRequests_NotPolled_SuppressesNoProgress()
    {
        // The producer is down, so its claimed requests not moving is the same fault: one alert, not two.
        await SeedSignalAsync(ShuffullSignalRules.SongRequestsPoll, s => s.RecordSuccess(Now.AddHours(-8)));
        await SeedSongRequestAsync(SongRequestStatus.Pending, Now.AddMinutes(-40));
        await SeedSongRequestAsync(SongRequestStatus.Accepted, Now.AddHours(-8));

        Assert.Equal("song-requests:not-polled", Assert.Single(await EvaluateAsync(SongRequests())).Key);
    }

    [Fact]
    public async Task SongRequests_ProducerDownWithNothingPending_StillAlertsNoProgress()
    {
        // not-polled needs something Pending, so with only claimed requests left no-progress is the alert that fires.
        await SeedSignalAsync(ShuffullSignalRules.SongRequestsPoll, s => s.RecordSuccess(Now.AddHours(-8)));
        await SeedSongRequestAsync(SongRequestStatus.Accepted, Now.AddHours(-8));

        var finding = Assert.Single(await EvaluateAsync(SongRequests()));
        Assert.Equal("song-requests:no-progress", finding.Key);
        Assert.Contains("last polled", finding.Body);
    }

    [Fact]
    public async Task SongRequests_StuckAndNoProgress_FireTogether()
    {
        // Claiming and the download lane break separately, so each keeps its own alert.
        await SeedSignalAsync(ShuffullSignalRules.SongRequestsPoll, s => s.RecordSuccess(Now.AddMinutes(-1)));
        await SeedSongRequestAsync(SongRequestStatus.Pending, Now.AddHours(-3));
        await SeedSongRequestAsync(SongRequestStatus.Retrying, Now.AddHours(-7));

        var keys = (await EvaluateAsync(SongRequests())).Select(f => f.Key).OrderBy(k => k, StringComparer.Ordinal);
        Assert.Equal(["song-requests:no-progress", "song-requests:stuck"], keys);
    }
}
