using Nut.Results;
using Shuffull.Core.Features.Listens.GetListenStats;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Features.Listens.GetListenStats;

/// <summary>
/// The stats query is raw SQL, so it is run on the production provider too: Postgres returns COUNT and SUM as
/// bigint, AVG over a CASE that mixes a numeric literal with double precision, and timestamps with a kind. Skipped
/// unless SHUFFULL_TEST_POSTGRES is set.
/// </summary>
public class GetListenStatsHandlerPostgresTest : IAsyncLifetime
{
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

    [PostgresFact]
    public async Task Stats_CountsAveragesAndFlag_OnPostgres()
    {
        var me = ListenFixture.NewUser();
        var other = ListenFixture.NewUser();
        var mine = new ListenFixture(me.UserId);
        var theirs = new ListenFixture(other.UserId);
        DateTime lastPlayed;
        await using (var seed = Database.CreateContext())
        {
            seed.Users.AddRange(me, other);
            mine.Add(seed, "song-a", ListenEndReason.Finished, 200);
            mine.Add(seed, "song-a", ListenEndReason.Previous, 100, duration: null);
            mine.AddQuickSkip(seed, "song-a");
            mine.AddQuickSkip(seed, "song-a");
            mine.AddQuickSkip(seed, "song-a", seconds: 29.9);
            lastPlayed = mine.LastStartedAt;
            mine.AddQuickSkip(seed, "song-b", seconds: 30);
            theirs.AddQuickSkip(seed, "song-b");
            theirs.AddQuickSkip(seed, "their-song");
            await seed.SaveChangesAsync();
        }

        await using var context = Database.CreateContext();
        var result = await new GetListenStatsHandler(context)
            .Handle(new GetListenStatsQuery(me.UserId, MaybeDropRule.Default), CancellationToken.None);

        Assert.True(result.IsOk, result.IsError ? result.GetError().Message : null);
        var songs = result.Get().Songs;
        Assert.Equal(["song-a", "song-b"], songs.Select(s => s.SongId));

        var a = songs[0];
        Assert.Equal((5, 3, 3, true), (a.Plays, a.Skips, a.RecentQuickSkips, a.MaybeDrop));
        Assert.Equal((200 + 100 + 5 + 5 + 29.9) / 5, a.AverageSecondsListened, 9);
        Assert.Equal((100 + 2.5 + 2.5 + 29.9 / 2) / 4, a.CompletionPercent!.Value, 9);
        Assert.Equal((lastPlayed, DateTimeKind.Utc), (a.LastPlayed, a.LastPlayed.Kind));

        var b = songs[1];
        Assert.Equal((1, 1, 0, false), (b.Plays, b.Skips, b.RecentQuickSkips, b.MaybeDrop));
    }
}
