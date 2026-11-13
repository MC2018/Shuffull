using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Channels;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shuffull.Api.Controllers;
using Shuffull.Api.Extensions;
using Shuffull.Api.Hubs;
using Shuffull.Api.Tools.Authorization;
using Shuffull.Core.Behaviors;
using Shuffull.Core.Features.Playback.GetPlaybackSessions;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence;

namespace Shuffull.Core.Tests.Api;

/// <summary>
/// The live channel (Shuffull#6) on a real test server with the real <see cref="JwtMiddleware"/>, the hub's sign-in
/// gate and SignalR (long polling, which the test server can carry). Shows that a write signals only the same user's
/// other connections, that presence feeds <c>online</c> and signals when it changes, that the <c>access_token</c> query
/// is honoured on the hub path and nowhere else, and that a connection without a valid token, or as another user's
/// device, is refused (503 when the sign-in can't be checked at all).
/// </summary>
public class PlaybackHubTest : IAsyncDisposable
{
    private const string TestSecret = "test-secret-key-that-is-long-enough-123456";
    private static readonly DateTime T0 = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    // A file, with a connection per request scope, rather than DatabaseFixture's one shared in-memory connection:
    // long-poll requests run the JWT and device lookups while a PUT is writing, and one SqliteConnection can't be
    // used from two threads at once.
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"shuffull-playback-hub-{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<ShuffullContext> _options;
    private readonly List<HubConnection> _connections = [];
    private WebApplication? _app;
    private TestServer? _server;

    public async ValueTask DisposeAsync()
    {
        foreach (var connection in _connections)
        {
            await connection.DisposeAsync();
        }
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
        SqliteConnection.ClearAllPools();
        File.Delete(_databasePath);
        GC.SuppressFinalize(this);
    }

    public PlaybackHubTest()
    {
        _options = new DbContextOptionsBuilder<ShuffullContext>().UseSqlite($"Data Source={_databasePath}").Options;
        using var context = new ShuffullContext(_options);
        context.Database.EnsureCreated();
    }

    /// <param name="authUnavailable">Build the server as if the sign-in store were down. Only the first call's value counts.</param>
    private async Task<TestServer> ServerAsync(bool authUnavailable = false)
    {
        if (_server is not null)
        {
            return _server;
        }

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        if (authUnavailable)
        {
            builder.Services.AddTransient<IStartupFilter, AuthUnavailableStartupFilter>();
        }
        builder.Configuration["Jwt:Secret"] = TestSecret;
        builder.Services.AddShuffullControllers().AddApplicationPart(typeof(UsersController).Assembly);
        var coreAssembly = typeof(ValidationBehavior<,>).Assembly;
        builder.Services.AddScoped(_ => new ShuffullContext(_options));
        builder.Services.AddValidatorsFromAssemblies([coreAssembly]);
        builder.Services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblies(coreAssembly);
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });
        builder.Services.AddPlaybackChannel();

        // The same order as Program.cs.
        _app = builder.Build();
        _app.UseRouting();
        _app.UseMiddleware<JwtMiddleware>();
        _app.UsePlaybackHubSignIn();
        _app.MapControllers();
        _app.MapPlaybackHub();
        await _app.StartAsync();
        _server = _app.GetTestServer();
        return _server;
    }

    private async Task<User> SeedUserAsync(string name, params string[] deviceIds)
    {
        await using var context = new ShuffullContext(_options);
        var user = new User { UserId = $"user-{name}", Username = name, Version = T0, ServerHash = "server-hash" };
        context.Users.Add(user);
        foreach (var deviceId in deviceIds)
        {
            context.UserDevices.Add(new UserDevice
            {
                UserId = user.UserId, DeviceId = deviceId, Name = deviceId, CreatedAt = T0, LastSeenAt = DateTime.UtcNow,
            });
        }
        await context.SaveChangesAsync();
        return user;
    }

    private async Task<string> TokenAsync(User user)
        => new JwtHelper((await ServerAsync()).Services).GenerateJwtToken(user, DateTime.UtcNow.AddHours(1));

    private async Task<HttpClient> ApiClientAsync(User user)
    {
        var client = (await ServerAsync()).CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(user));
        return client;
    }

    /// <summary>A live connection as <paramref name="deviceId"/>, recording every signal it gets.</summary>
    private sealed record Listener(HubConnection Connection, ChannelReader<string> Signals);

    /// <param name="tokenInQuery">True: the token rides in <c>?access_token=</c>, as a browser's WebSocket sends it.
    /// False: in the Authorization header, as the client sends it on long polling.</param>
    private async Task<Listener> ConnectAsync(User user, string deviceId, bool tokenInQuery = true, string? token = null)
    {
        var server = await ServerAsync();
        token ??= await TokenAsync(user);
        var query = $"deviceId={Uri.EscapeDataString(deviceId)}" + (tokenInQuery ? $"&access_token={Uri.EscapeDataString(token)}" : "");
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, $"{PlaybackHub.Path}?{query}"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                if (!tokenInQuery)
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(token);
                }
            })
            .Build();
        _connections.Add(connection);
        var signals = Channel.CreateUnbounded<string>();
        connection.On<string>(nameof(IPlaybackClient.SessionChanged), id => signals.Writer.TryWrite(id));
        await connection.StartAsync();
        return new Listener(connection, signals.Reader);
    }

    private async Task PutStateAsync(User user, string deviceId, string songId)
    {
        var response = await (await ApiClientAsync(user)).PutAsJsonAsync(
            $"/api/v1/playback/sessions/{deviceId}", new { songId, positionSeconds = 0, isPlaying = true });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<string> NextSignalAsync(Listener listener)
    {
        using var timeout = new CancellationTokenSource(Wait);
        return await listener.Signals.ReadAsync(timeout.Token);
    }

    [Fact]
    public async Task AWrite_SignalsOnlyTheSameUsersOtherDevices()
    {
        var a = await SeedUserAsync("a", "phone", "laptop", "tablet");
        var b = await SeedUserAsync("b", "b-phone", "b-laptop");
        var phone = await ConnectAsync(a, "phone");
        var laptop = await ConnectAsync(a, "laptop");
        var tablet = await ConnectAsync(a, "tablet", tokenInQuery: false);
        var bPhone = await ConnectAsync(b, "b-phone");
        // Connecting is itself a change (the device is now online): the phone hears the laptop and the tablet arrive,
        // and the laptop hears the tablet. The two sends to the phone come from different connections, so in any order.
        Assert.Equal(["laptop", "tablet"], new[] { await NextSignalAsync(phone), await NextSignalAsync(phone) }.Order());
        Assert.Equal("tablet", await NextSignalAsync(laptop));

        await PutStateAsync(a, "phone", "song-1");

        Assert.Equal("phone", await NextSignalAsync(laptop));
        Assert.Equal("phone", await NextSignalAsync(tablet));

        // Signals to one connection arrive in order. So if the next thing the phone and B's phone hear is the write
        // that follows, the phone's own write never reached the phone, and A's write never reached B.
        await PutStateAsync(a, "laptop", "song-2");
        await PutStateAsync(b, "b-laptop", "song-3");
        Assert.Equal("laptop", await NextSignalAsync(phone));
        Assert.Equal("b-laptop", await NextSignalAsync(bPhone));
        Assert.Equal("laptop", await NextSignalAsync(tablet));
        Assert.False(laptop.Signals.TryRead(out _));
    }

    [Fact]
    public async Task Presence_MarksConnectedDevicesOnline_UntilTheyDisconnect()
    {
        var a = await SeedUserAsync("a", "phone", "laptop");
        var b = await SeedUserAsync("b", "phone");
        var aPhone = await ConnectAsync(a, "phone");
        await ConnectAsync(b, "phone");
        var client = await ApiClientAsync(a);

        async Task<Dictionary<string, bool>> OnlineAsync()
            => (await client.GetFromJsonAsync<GetPlaybackSessionsResponse>("/api/v1/playback/sessions"))!
                .Devices.ToDictionary(d => d.DeviceId, d => d.Online);

        Assert.Equal(new Dictionary<string, bool> { ["phone"] = true, ["laptop"] = false }, await OnlineAsync());

        await aPhone.Connection.StopAsync();
        var deadline = DateTime.UtcNow + Wait;
        while ((await OnlineAsync())["phone"] && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }
        Assert.Equal(new Dictionary<string, bool> { ["phone"] = false, ["laptop"] = false }, await OnlineAsync());
    }

    [Fact]
    public async Task Presence_Changes_SignalTheUsersOtherDevices_WhenADeviceConnects_AndWhenItsLastConnectionCloses()
    {
        var a = await SeedUserAsync("a", "phone", "laptop", "tablet");
        var b = await SeedUserAsync("b", "b-phone");
        var laptop = await ConnectAsync(a, "laptop");

        // Each connection of the phone tells the laptop the phone's row (its online) changed; B's device tells it nothing.
        var phone1 = await ConnectAsync(a, "phone");
        Assert.Equal("phone", await NextSignalAsync(laptop));
        var phone2 = await ConnectAsync(a, "phone");
        Assert.Equal("phone", await NextSignalAsync(laptop));
        await ConnectAsync(b, "b-phone");

        // Closing one of the phone's two connections leaves it online: no signal. Signals to one connection arrive in
        // order, so if the next thing the laptop hears is the tablet's write, neither B's connection nor that close
        // signalled it.
        await phone1.Connection.StopAsync();
        await PutStateAsync(a, "tablet", "song-1");
        Assert.Equal("tablet", await NextSignalAsync(laptop));

        // Closing the last one takes the phone offline, and the laptop hears about it.
        await phone2.Connection.StopAsync();
        Assert.Equal("phone", await NextSignalAsync(laptop));
        Assert.False(laptop.Signals.TryRead(out _));
    }

    [Fact]
    public async Task TheQueryToken_IsIgnoredOutsideTheHubPath()
    {
        var a = await SeedUserAsync("a", "phone");
        var client = (await ServerAsync()).CreateClient();

        var response = await client.GetAsync($"/api/v1/playback/sessions?access_token={Uri.EscapeDataString(await TokenAsync(a))}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Connecting_WithoutAValidToken_IsRefused()
    {
        var a = await SeedUserAsync("a", "phone");
        var client = (await ServerAsync()).CreateClient();

        var anonymous = await client.PostAsync($"{PlaybackHub.Path}/negotiate?negotiateVersion=1&deviceId=phone", null);
        var forged = await client.PostAsync($"{PlaybackHub.Path}/negotiate?negotiateVersion=1&deviceId=phone&access_token=not-a-token", null);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);
        var refused = await Assert.ThrowsAsync<HttpRequestException>(() => ConnectAsync(a, "phone", token: "not-a-token"));
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    [Fact]
    public async Task Connecting_WhileTheSignInCantBeChecked_Is503_NotA401()
    {
        await SeedUserAsync("a", "phone");
        var client = (await ServerAsync(authUnavailable: true)).CreateClient();

        var response = await client.PostAsync($"{PlaybackHub.Path}/negotiate?negotiateVersion=1&deviceId=phone", null);

        // A 401 tells the app to sign the user out; a 503 tells it to retry.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Connecting_AsAnotherUsersDevice_OrWithoutADevice_IsRefused()
    {
        var a = await SeedUserAsync("a", "a-phone");
        await SeedUserAsync("b", "b-phone");
        var client = (await ServerAsync()).CreateClient();
        var token = Uri.EscapeDataString(await TokenAsync(a));

        var noDevice = await client.PostAsync($"{PlaybackHub.Path}/negotiate?negotiateVersion=1&access_token={token}", null);

        Assert.Equal(HttpStatusCode.BadRequest, noDevice.StatusCode);
        var refused = await Assert.ThrowsAsync<HttpRequestException>(() => ConnectAsync(a, "b-phone"));
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }
}
