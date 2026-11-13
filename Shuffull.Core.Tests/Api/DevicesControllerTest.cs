using System.Net;
using System.Net.Http.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shuffull.Core.Behaviors;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Api;

/// <summary>
/// The device check-in (Shuffull#132) through the real controller, binding, auth filter and validation pipeline, on
/// SQLite: one row per (user, device), updated in place, never shared across users, and a 400 for a bad id or name.
/// </summary>
public class DevicesControllerTest : IAsyncDisposable
{
    private static readonly DateTime T0 = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);

    private readonly DatabaseFixture _database = new();
    private readonly ManualTimeProvider _clock = new(T0);
    private readonly Dictionary<string, ApiTestHost> _hosts = [];

    public async ValueTask DisposeAsync()
    {
        foreach (var host in _hosts.Values)
        {
            await host.DisposeAsync();
        }
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<User> SeedUserAsync(string name)
    {
        var user = new User { UserId = $"user-{name}", Username = name, Version = T0, ServerHash = "server-hash" };
        _database.Context.Users.Add(user);
        await _database.Context.SaveChangesAsync();
        return user;
    }

    /// <summary>A test server signed in as <paramref name="user"/> (null = no token), all over the same database.</summary>
    private async Task<HttpClient> ClientAsync(User? user)
    {
        var key = user?.UserId ?? "";
        if (!_hosts.TryGetValue(key, out var host))
        {
            host = await ApiTestHost.StartAsync(services =>
            {
                // The real pipeline, as in Program.cs: validators run before each handler.
                var coreAssembly = typeof(ValidationBehavior<,>).Assembly;
                services.AddScoped(_ => _database.CreateContext());
                services.AddSingleton<TimeProvider>(_clock);
                services.AddValidatorsFromAssemblies([coreAssembly]);
                services.AddMediatR(cfg =>
                {
                    cfg.RegisterServicesFromAssemblies(coreAssembly);
                    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
                });
            }, user);
            _hosts[key] = host;
        }
        return host.Client;
    }

    private async Task<HttpResponseMessage> PutAsync(User? user, string deviceId, object body)
        => await (await ClientAsync(user)).PutAsJsonAsync($"/api/v1/devices/{Uri.EscapeDataString(deviceId)}", body);

    private async Task<UserDeviceDto> CheckInOkAsync(User user, string deviceId, string name, string? platform = "android")
    {
        var response = await PutAsync(user, deviceId, new { name, platform });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<UserDeviceDto>())!;
    }

    private async Task<List<UserDevice>> RowsAsync()
    {
        await using var context = _database.CreateContext();
        return await context.UserDevices.AsNoTracking().OrderBy(d => d.UserId).ThenBy(d => d.DeviceId).ToListAsync();
    }

    [Fact]
    public async Task CheckIn_CreatesTheRow_ThenUpdatesItInPlace_AndMovesLastSeenAt()
    {
        var user = await SeedUserAsync("a");

        var first = await CheckInOkAsync(user, "device-1", "Pixel 8");
        _clock.UtcNow = T0.AddMinutes(10);
        var second = await CheckInOkAsync(user, "device-1", "  Living room  ", "web");

        Assert.Equal(new UserDeviceDto("device-1", "Pixel 8", "android", T0, T0), first);
        Assert.Equal(new UserDeviceDto("device-1", "Living room", "web", T0, T0.AddMinutes(10)), second);

        var row = Assert.Single(await RowsAsync());
        Assert.Equal((user.UserId, "device-1", "Living room", "web", T0, T0.AddMinutes(10)),
            (row.UserId, row.DeviceId, row.Name, row.Platform, row.CreatedAt, row.LastSeenAt));
    }

    [Fact]
    public async Task CheckIn_TheSameDeviceIdForTwoUsers_MakesTwoRows_AndNeitherTouchesTheOther()
    {
        var a = await SeedUserAsync("a");
        var b = await SeedUserAsync("b");

        await CheckInOkAsync(a, "shared-install", "A's name");
        _clock.UtcNow = T0.AddMinutes(1);
        await CheckInOkAsync(b, "shared-install", "B's name", "desktop");

        Assert.Equal(
            [(a.UserId, "A's name", "android", T0), (b.UserId, "B's name", "desktop", T0.AddMinutes(1))],
            (await RowsAsync()).Select(d => (d.UserId, d.Name, d.Platform, d.LastSeenAt)));
    }

    [Fact]
    public async Task CheckIn_WithoutAPlatform_StoresNull()
    {
        var user = await SeedUserAsync("a");

        await CheckInOkAsync(user, "device-1", "Pixel 8", platform: null);

        Assert.Null(Assert.Single(await RowsAsync()).Platform);
    }

    public static TheoryData<string, string?, string?> BadCheckIns => new()
    {
        { " ", "Pixel 8", "android" },
        { new string('d', UserDevice.MaxDeviceIdLength + 1), "Pixel 8", "android" },
        { "device\u0001", "Pixel 8", "android" },
        { "device-1", "", "android" },
        { "device-1", "   ", "android" },
        { "device-1", null, "android" },
        { "device-1", new string('n', UserDevice.MaxNameLength + 1), "android" },
        { "device-1", "Pixel\n8", "android" },
        { "device-1", "Pixel 8", new string('p', Listen.MaxPlatformLength + 1) },
    };

    [Theory]
    [MemberData(nameof(BadCheckIns))]
    public async Task CheckIn_WithAnInvalidValue_Is400_AndWritesNothing(string deviceId, string? name, string? platform)
    {
        var user = await SeedUserAsync("a");

        var response = await PutAsync(user, deviceId, new { name, platform });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await RowsAsync());
    }

    [Fact]
    public async Task CheckIn_AtTheMaximumLengths_IsAccepted()
    {
        var user = await SeedUserAsync("a");
        var deviceId = new string('d', UserDevice.MaxDeviceIdLength);
        var name = new string('n', UserDevice.MaxNameLength);

        await CheckInOkAsync(user, deviceId, name, new string('p', Listen.MaxPlatformLength));

        Assert.Equal(deviceId, Assert.Single(await RowsAsync()).DeviceId);
    }

    [Fact]
    public async Task CheckIn_WithoutAToken_Is401()
    {
        var response = await PutAsync(null, "device-1", new { name = "Pixel 8", platform = "android" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await RowsAsync());
    }
}
