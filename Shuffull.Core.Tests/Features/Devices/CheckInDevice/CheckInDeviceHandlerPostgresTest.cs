using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Features.Devices.CheckInDevice;
using Shuffull.Core.Features.Listens.RecordListens;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Features.Devices.CheckInDevice;

/// <summary>
/// The device check-in on real Postgres, built by the real migrations (so this also proves AddUserDevices applies).
/// Covers the upsert's SQL on the provider prod runs, and check-ins from one install arriving at once, which must
/// leave one row rather than fail the key. Skipped unless SHUFFULL_TEST_POSTGRES is set.
/// </summary>
public class CheckInDeviceHandlerPostgresTest : IAsyncLifetime
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

    private async Task<User> SeedUserAsync(string name)
    {
        await using var context = Database.CreateContext();
        var user = new User { UserId = $"user-{name}", Username = name, Version = T0, ServerHash = "server-hash" };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    private async Task<Result<UserDeviceDto>> CheckInAsync(string userId, string deviceId, string name, DateTime now)
    {
        await using var context = Database.CreateContext();
        return await new CheckInDeviceHandler(context, new ManualTimeProvider(now))
            .Handle(new CheckInDeviceCommand(userId, deviceId, name, "android"), CancellationToken.None);
    }

    [PostgresFact]
    public async Task CheckIn_CreatesThenUpdates_PerUser()
    {
        var a = await SeedUserAsync("a");
        var b = await SeedUserAsync("b");

        Assert.Equal(new UserDeviceDto("device-1", "Pixel 8", "android", T0, T0), (await CheckInAsync(a.UserId, "device-1", "Pixel 8", T0)).Get());
        Assert.Equal(new UserDeviceDto("device-1", "Renamed", "android", T0, T0.AddHours(1)),
            (await CheckInAsync(a.UserId, "device-1", "Renamed", T0.AddHours(1))).Get());
        Assert.True((await CheckInAsync(b.UserId, "device-1", "B's", T0.AddHours(2))).IsOk);

        await using var check = Database.CreateContext();
        Assert.Equal(
            [(a.UserId, "Renamed", T0, T0.AddHours(1)), (b.UserId, "B's", T0.AddHours(2), T0.AddHours(2))],
            (await check.UserDevices.OrderBy(d => d.UserId).ToListAsync()).Select(d => (d.UserId, d.Name, d.CreatedAt, d.LastSeenAt)));
    }

    [PostgresFact]
    public async Task CheckIn_ManyAtOnceFromOneInstall_LeavesOneRow()
    {
        var user = await SeedUserAsync("a");

        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(i => CheckInAsync(user.UserId, "device-1", $"Name {i}", T0.AddSeconds(i))));

        Assert.All(results, r => Assert.True(r.IsOk, r.IsError ? r.GetError().Message : null));
        await using var check = Database.CreateContext();
        Assert.Single(await check.UserDevices.ToListAsync());
    }

    [PostgresFact]
    public async Task Listen_WithADeviceId_IsStored()
    {
        var user = await SeedUserAsync("a");
        await using var context = Database.CreateContext();

        var result = await new RecordListensHandler(context).Handle(new RecordListensCommand(user.UserId,
        [
            new ListenItem("with-device", "song-1", T0, T0.AddSeconds(30), 30, 200, "skipped", "playlist", DeviceId: "device-1"),
            new ListenItem("old-app", "song-1", T0, T0.AddSeconds(30), 30, 200, "skipped", "playlist"),
        ]), CancellationToken.None);

        Assert.True(result.IsOk);
        Assert.All(result.Get().Results, r => Assert.Equal(ListenOutcomes.Recorded, r.Outcome));
        await using var check = Database.CreateContext();
        Assert.Equal([("old-app", null), ("with-device", "device-1")],
            (await check.Listens.OrderBy(l => l.ListenId).ToListAsync()).Select(l => (l.ListenId, l.DeviceId)));
    }
}
