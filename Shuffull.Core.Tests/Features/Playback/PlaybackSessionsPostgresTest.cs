using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Nut.Results;
using Shuffull.Api.Services;
using Shuffull.Core.Errors;
using Shuffull.Core.Features.Playback.TakeOverPlaybackSession;
using Shuffull.Core.Features.Playback.UpdatePlaybackSession;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Persistence;
using Shuffull.Core.Services;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Features.Playback;

/// <summary>
/// Playback sessions (Shuffull#6) on real Postgres, built by the real migrations (so this also proves
/// AddPlaybackSessions applies). Covers what SQLite can't show: the <c>text[]</c> columns, the takeover's equality
/// check against a <c>timestamptz</c>, first writes from one device racing on the key, and the prune's delete.
/// Skipped unless SHUFFULL_TEST_POSTGRES is set.
/// </summary>
public class PlaybackSessionsPostgresTest : IAsyncLifetime
{
    private static readonly DateTime T0 = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);

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

    private async Task<User> SeedUserAsync(string name, params string[] deviceIds)
    {
        await using var context = Database.CreateContext();
        var user = new User { UserId = $"user-{name}", Username = name, Version = T0, ServerHash = "server-hash" };
        context.Users.Add(user);
        foreach (var deviceId in deviceIds)
        {
            context.UserDevices.Add(new UserDevice { UserId = user.UserId, DeviceId = deviceId, Name = deviceId, CreatedAt = T0, LastSeenAt = T0 });
        }
        await context.SaveChangesAsync();
        return user;
    }

    private async Task<Result<PlaybackSessionDto>> PutAsync(string userId, string deviceId, DateTime now, string songId = "song-1",
        string? scope = null, string[]? history = null, string[]? queuedNext = null, DateTime? seenTakenOverAt = null)
    {
        await using var context = Database.CreateContext();
        return await new UpdatePlaybackSessionHandler(context, new ManualTimeProvider(now), new NoSignals())
            .Handle(new UpdatePlaybackSessionCommand(userId, deviceId, songId, 30, true, scope, history ?? [], queuedNext ?? [],
                    seenTakenOverAt),
                CancellationToken.None);
    }

    private async Task<Result<PlaybackSessionDto>> TakeOverAsync(string userId, string deviceId, string byDeviceId, DateTime expected, DateTime now)
    {
        await using var context = Database.CreateContext();
        return await new TakeOverPlaybackSessionHandler(context, new ManualTimeProvider(now), new NoSignals())
            .Handle(new TakeOverPlaybackSessionCommand(userId, deviceId, byDeviceId, expected), CancellationToken.None);
    }

    [PostgresFact]
    public async Task Put_ThenRead_RoundTripsTheArraysAndScope()
    {
        var user = await SeedUserAsync("a", "phone");
        const string scope = """{"kind":"playlist","tags":["ロック","Rock"]}""";

        var answer = (await PutAsync(user.UserId, "phone", T0, scope: scope, history: ["song-a", "song-b"], queuedNext: ["song-c"])).Get();

        Assert.Equal(["song-a", "song-b"], answer.History);
        Assert.Equal(["song-c"], answer.QueuedNext);
        await using var check = Database.CreateContext();
        var row = await check.PlaybackSessions.AsNoTracking().SingleAsync();
        Assert.Equal(["song-a", "song-b"], row.History);
        Assert.Equal(["song-c"], row.QueuedNext);
        Assert.Equal("ロック", JsonDocument.Parse(row.Scope!).RootElement.GetProperty("tags")[0].GetString());
    }

    [PostgresFact]
    public async Task TakeOver_MatchesTheStoredTimestampExactly_AndAStaleOneIs409()
    {
        var user = await SeedUserAsync("a", "phone", "laptop");
        // A clock with sub-millisecond ticks: the stored value is cut to whole milliseconds.
        var read = (await PutAsync(user.UserId, "phone", T0.AddTicks(12_345))).Get();
        Assert.Equal(T0.AddMilliseconds(1), read.UpdatedAt);

        var stale = await TakeOverAsync(user.UserId, "phone", "laptop", read.UpdatedAt.AddMilliseconds(-1), T0.AddSeconds(1));
        Assert.True(stale.IsError);
        Assert.Equal(ErrorKind.Conflict, Assert.IsType<KindedError>(stale.GetError()).Kind);

        var taken = await TakeOverAsync(user.UserId, "phone", "laptop", read.UpdatedAt, T0.AddSeconds(1));
        Assert.True(taken.IsOk, taken.IsError ? taken.GetError().Message : null);
        Assert.Equal(("laptop", false), (taken.Get().TakenOverBy, taken.Get().IsPlaying));

        // The source retires the takeover by echoing its timestamp: the same timestamptz equality, so a stale echo
        // (a millisecond off) leaves it and the exact one clears it.
        var staleEcho = (await PutAsync(user.UserId, "phone", T0.AddSeconds(2), seenTakenOverAt: taken.Get().TakenOverAt!.Value.AddMilliseconds(-1))).Get();
        Assert.Equal("laptop", staleEcho.TakenOverBy);
        var retired = (await PutAsync(user.UserId, "phone", T0.AddSeconds(3), seenTakenOverAt: taken.Get().TakenOverAt)).Get();
        Assert.Null(retired.TakenOverBy);
        Assert.Null(retired.TakenOverAt);
    }

    [PostgresFact]
    public async Task FirstPuts_ManyAtOnceFromOneDevice_LeaveOneRow()
    {
        var user = await SeedUserAsync("a", "phone");

        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(i => PutAsync(user.UserId, "phone", T0.AddSeconds(i), songId: $"song-{i}")));

        Assert.All(results, r => Assert.True(r.IsOk, r.IsError ? r.GetError().Message : null));
        await using var check = Database.CreateContext();
        Assert.Single(await check.PlaybackSessions.ToListAsync());
    }

    [PostgresFact]
    public async Task Prune_DeletesByUpdatedAt_AndKeepsTheDevices()
    {
        var user = await SeedUserAsync("a", "stale", "fresh");
        await PutAsync(user.UserId, "stale", T0);
        await PutAsync(user.UserId, "fresh", T0.AddDays(5));
        var services = new ServiceCollection().AddScoped<ShuffullContext>(_ => Database.CreateContext()).BuildServiceProvider();
        var prune = new PlaybackSessionPruneService(services, TimeProvider.System, NullLogger<PlaybackSessionPruneService>.Instance);

        Assert.Equal(0, await prune.PruneAsync(T0.AddDays(30), CancellationToken.None));
        Assert.Equal(1, await prune.PruneAsync(T0.AddDays(30).AddMilliseconds(1), CancellationToken.None));

        await using var check = Database.CreateContext();
        Assert.Equal(["fresh"], await check.PlaybackSessions.Select(s => s.DeviceId).ToListAsync());
        Assert.Equal(2, await check.UserDevices.CountAsync());
    }

    private sealed class NoSignals : IPlaybackNotifier
    {
        public Task SessionChangedAsync(string userId, string deviceId, string? writerDeviceId) => Task.CompletedTask;
    }
}
