using Nut.Results;
using Shuffull.Core.Features.Listens.GetListenStats;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Features.Listens.GetListenStats;

/// <summary>
/// Per-song stats derived from a fixture of listens: the counts and averages, and the "maybe drop this" flag firing
/// exactly at its threshold (skipped under 30 s on 3 of the last 5 plays) and not one listen before.
/// </summary>
public class GetListenStatsHandlerTest : IDisposable
{
    private readonly DatabaseFixture _database = new();
    private readonly User _me;
    private readonly ListenFixture _listens;

    public GetListenStatsHandlerTest()
    {
        _me = ListenFixture.NewUser();
        _database.Context.Users.Add(_me);
        _database.Context.SaveChanges();
        _listens = new ListenFixture(_me.UserId);
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<GetListenStatsResponse> StatsAsync(MaybeDropRule? rule = null, string? userId = null)
    {
        await _database.Context.SaveChangesAsync();
        await using var context = _database.CreateContext();
        var result = await new GetListenStatsHandler(context)
            .Handle(new GetListenStatsQuery(userId ?? _me.UserId, rule ?? MaybeDropRule.Default), CancellationToken.None);
        Assert.True(result.IsOk, result.IsError ? result.GetError().Message : null);
        return result.Get();
    }

    private async Task<SongListenStats> SongStatsAsync(string songId, MaybeDropRule? rule = null)
        => Assert.Single((await StatsAsync(rule)).Songs, s => s.SongId == songId);

    [Fact]
    public async Task Stats_CountPlaysAndSkips_AverageSeconds_CompletionAndLastPlayed()
    {
        var c = _database.Context;
        _listens.Add(c, "song-a", ListenEndReason.Finished, 200);
        _listens.Add(c, "song-a", ListenEndReason.Skipped, 50);
        _listens.Add(c, "song-a", ListenEndReason.Previous, 100);
        // More than the song's length (a wrong duration from the app): completion caps at 100% for this listen.
        _listens.Add(c, "song-a", ListenEndReason.Stopped, 300);
        var lastPlayed = _listens.LastStartedAt;

        var a = await SongStatsAsync("song-a");

        Assert.Equal(4, a.Plays);
        Assert.Equal(1, a.Skips); // "previous" and "stopped" are not skips
        Assert.Equal((200 + 50 + 100 + 300) / 4.0, a.AverageSecondsListened, 9);
        Assert.Equal((100 + 25 + 50 + 100) / 4.0, a.CompletionPercent!.Value, 9);
        Assert.Equal(lastPlayed, a.LastPlayed);
        Assert.Equal(DateTimeKind.Utc, a.LastPlayed.Kind);
        Assert.False(a.MaybeDrop);
    }

    [Fact]
    public async Task Completion_CountsOnlyListensThatKnewTheLength_AndIsNullWhenNoneDid()
    {
        var c = _database.Context;
        _listens.Add(c, "song-known", ListenEndReason.Finished, 100, duration: 100);
        _listens.Add(c, "song-known", ListenEndReason.Skipped, 10, duration: null);
        _listens.Add(c, "song-known", ListenEndReason.Skipped, 10, duration: 0);
        _listens.Add(c, "song-unknown", ListenEndReason.Finished, 100, duration: null);

        var songs = (await StatsAsync()).Songs;

        Assert.Equal(100, Assert.Single(songs, s => s.SongId == "song-known").CompletionPercent);
        Assert.Null(Assert.Single(songs, s => s.SongId == "song-unknown").CompletionPercent);
    }

    [Fact]
    public async Task MaybeDrop_FiresAtTheThirdQuickSkipOfTheLastFive_NotTheSecond()
    {
        var c = _database.Context;
        _listens.AddFinished(c, "song");
        _listens.AddFinished(c, "song");
        _listens.AddFinished(c, "song");
        _listens.AddQuickSkip(c, "song");
        _listens.AddQuickSkip(c, "song");

        var two = await SongStatsAsync("song");
        Assert.Equal((2, false), (two.RecentQuickSkips, two.MaybeDrop));

        _listens.AddQuickSkip(c, "song"); // the last five are now finished, finished, skip, skip, skip

        var three = await SongStatsAsync("song");
        Assert.Equal((3, true), (three.RecentQuickSkips, three.MaybeDrop));
    }

    [Fact]
    public async Task MaybeDrop_ASkipAtExactlyThirtySeconds_IsNotQuick()
    {
        var c = _database.Context;
        _listens.AddQuickSkip(c, "at-limit", seconds: 30);
        _listens.AddQuickSkip(c, "at-limit", seconds: 30);
        _listens.AddQuickSkip(c, "at-limit", seconds: 30);
        _listens.AddQuickSkip(c, "under-limit", seconds: 29.9);
        _listens.AddQuickSkip(c, "under-limit", seconds: 29.9);
        _listens.AddQuickSkip(c, "under-limit", seconds: 29.9);

        var songs = (await StatsAsync()).Songs;

        var atLimit = Assert.Single(songs, s => s.SongId == "at-limit");
        Assert.Equal((3, 0, false), (atLimit.Skips, atLimit.RecentQuickSkips, atLimit.MaybeDrop));
        Assert.True(Assert.Single(songs, s => s.SongId == "under-limit").MaybeDrop);
    }

    [Fact]
    public async Task MaybeDrop_QuickSkipsOlderThanTheLastFive_DoNotCount()
    {
        var c = _database.Context;
        for (var i = 0; i < 3; i++)
        {
            _listens.AddQuickSkip(c, "song");
        }
        for (var i = 0; i < 5; i++)
        {
            _listens.AddFinished(c, "song");
        }

        var song = await SongStatsAsync("song");

        Assert.Equal((8, 3, 0, false), (song.Plays, song.Skips, song.RecentQuickSkips, song.MaybeDrop));
    }

    [Fact]
    public async Task MaybeDrop_QuickPreviousOrStop_IsNotASkip()
    {
        var c = _database.Context;
        _listens.Add(c, "song", ListenEndReason.Previous, 5);
        _listens.Add(c, "song", ListenEndReason.Stopped, 5);
        _listens.Add(c, "song", ListenEndReason.Previous, 5);
        _listens.AddQuickSkip(c, "song");
        _listens.AddQuickSkip(c, "song");

        var song = await SongStatsAsync("song");

        Assert.Equal((2, false), (song.RecentQuickSkips, song.MaybeDrop));
    }

    [Fact]
    public async Task MaybeDrop_ASongWithOnlyThreePlays_AllQuickSkips_IsFlagged()
    {
        var c = _database.Context;
        _listens.AddQuickSkip(c, "song");
        _listens.AddQuickSkip(c, "song");
        _listens.AddQuickSkip(c, "song");

        Assert.True((await SongStatsAsync("song")).MaybeDrop);
    }

    [Fact]
    public async Task Stats_UseTheRuleTheyAreGiven_AndEchoIt()
    {
        _listens.AddQuickSkip(_database.Context, "song", seconds: 8);
        var rule = new MaybeDropRule(SkippedUnderSeconds: 10, QuickSkips: 1, OfLastPlays: 1);

        var stats = await StatsAsync(rule);

        Assert.Equal(rule, stats.MaybeDropRule);
        Assert.True(Assert.Single(stats.Songs).MaybeDrop);
    }

    [Fact]
    public async Task Stats_NeverCountAnotherUsersListens()
    {
        var other = ListenFixture.NewUser();
        _database.Context.Users.Add(other);
        var theirs = new ListenFixture(other.UserId);
        var c = _database.Context;
        _listens.AddFinished(c, "shared-song");
        for (var i = 0; i < 3; i++)
        {
            theirs.AddQuickSkip(c, "shared-song");
            theirs.AddQuickSkip(c, "their-song");
        }

        var mine = await StatsAsync();

        var shared = Assert.Single(mine.Songs);
        Assert.Equal(("shared-song", 1, 0, false), (shared.SongId, shared.Plays, shared.Skips, shared.MaybeDrop));
    }

    [Fact]
    public async Task Stats_NoListens_IsEmpty()
        => Assert.Empty((await StatsAsync()).Songs);

    [Fact]
    public async Task Stats_AreOrderedBySongId()
    {
        var c = _database.Context;
        _listens.AddFinished(c, "song-c");
        _listens.AddFinished(c, "song-a");
        _listens.AddFinished(c, "song-b");

        Assert.Equal(["song-a", "song-b", "song-c"], (await StatsAsync()).Songs.Select(s => s.SongId));
    }
}
