using Shuffull.Core.Services;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shuffull.Api.Services;
using Shuffull.Core.Behaviors;
using Shuffull.Core.Features.Playback.GetPlaybackSessions;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Persistence;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Api;

/// <summary>
/// Playback sessions (Shuffull#6) through the real controller, binding, auth filter and validation pipeline, on
/// SQLite: the state round trip and position estimate, a takeover that only lands against the row it read, a command
/// that only its own ack clears, the signals each write sends, and that no route reads or writes another user's
/// sessions or targets another user's device.
/// </summary>
public class PlaybackControllerTest : IAsyncDisposable
{
    private static readonly DateTime T0 = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);

    private readonly DatabaseFixture _database = new();
    private readonly ManualTimeProvider _clock = new(T0);
    private readonly RecordingPlaybackNotifier _notifier = new();
    private readonly FakePlaybackPresence _presence = new();
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

    private async Task<User> SeedUserAsync(string name, params string[] deviceIds)
    {
        var user = new User { UserId = $"user-{name}", Username = name, Version = T0, ServerHash = "server-hash" };
        _database.Context.Users.Add(user);
        foreach (var deviceId in deviceIds)
        {
            _database.Context.UserDevices.Add(new UserDevice
            {
                UserId = user.UserId, DeviceId = deviceId, Name = $"{name}'s {deviceId}", Platform = "android",
                CreatedAt = T0, LastSeenAt = T0,
            });
        }
        await _database.Context.SaveChangesAsync();
        return user;
    }

    private async Task<HttpClient> ClientAsync(User? user)
    {
        var key = user?.UserId ?? "";
        if (!_hosts.TryGetValue(key, out var host))
        {
            host = await ApiTestHost.StartAsync(services =>
            {
                var coreAssembly = typeof(ValidationBehavior<,>).Assembly;
                services.AddScoped(_ => _database.CreateContext());
                services.AddSingleton<TimeProvider>(_clock);
                services.AddSingleton<IPlaybackNotifier>(_notifier);
                services.AddSingleton<IPlaybackPresence>(_presence);
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

    private static string SessionUrl(string deviceId) => $"/api/v1/playback/sessions/{Uri.EscapeDataString(deviceId)}";

    private static object State(string? songId = "song-1", double position = 30, bool playing = true, object? scope = null,
        string[]? history = null, string[]? queuedNext = null)
        => new { songId, positionSeconds = position, isPlaying = playing, scope, history, queuedNext };

    private async Task<HttpResponseMessage> PutAsync(User? user, string deviceId, object body)
        => await (await ClientAsync(user)).PutAsJsonAsync(SessionUrl(deviceId), body);

    private async Task<PlaybackSessionDto> PutOkAsync(User user, string deviceId, object body)
    {
        var response = await PutAsync(user, deviceId, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PlaybackSessionDto>())!;
    }

    private async Task<HttpResponseMessage> GetAsync(User? user) => await (await ClientAsync(user)).GetAsync("/api/v1/playback/sessions");

    private async Task<GetPlaybackSessionsResponse> GetOkAsync(User user)
    {
        var response = await GetAsync(user);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<GetPlaybackSessionsResponse>())!;
    }

    private async Task<HttpResponseMessage> TakeOverAsync(User user, string deviceId, string? byDeviceId, DateTime? expectedUpdatedAt)
        => await (await ClientAsync(user)).PostAsJsonAsync($"{SessionUrl(deviceId)}/takeover", new { byDeviceId, expectedUpdatedAt });

    private async Task<HttpResponseMessage> CommandAsync(User user, string deviceId, string? commandId, string? kind, double? seekSeconds = null)
        => await (await ClientAsync(user)).PostAsJsonAsync($"{SessionUrl(deviceId)}/command", new { commandId, kind, seekSeconds });

    private async Task<HttpResponseMessage> AckAsync(User user, string deviceId, string commandId)
        => await (await ClientAsync(user)).PostAsync($"{SessionUrl(deviceId)}/command/{Uri.EscapeDataString(commandId)}/ack", null);

    private async Task<PlaybackSession?> RowAsync(string userId, string deviceId)
    {
        await using var context = _database.CreateContext();
        return await context.PlaybackSessions.AsNoTracking().SingleOrDefaultAsync(s => s.UserId == userId && s.DeviceId == deviceId);
    }

    // --- PUT + GET ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Put_ThenGet_RoundTripsTheState_AndEstimatesThePositionFromTheServerClock()
    {
        var user = await SeedUserAsync("a", "phone", "laptop");
        var scope = new { genres = new[] { "rock", "j-pop" }, minRating = 3, nested = new { liked = true } };

        var written = await PutOkAsync(user, "phone", State("song-9", 30, true, scope, ["song-1", "song-2"], ["song-10", "song-11"]));
        _clock.UtcNow = T0.AddSeconds(12.5);
        var listing = await GetOkAsync(user);

        Assert.Equal((T0, T0, 30d), (written.PositionAt, written.UpdatedAt, written.EstimatedPositionSeconds));
        Assert.Equal(T0.AddSeconds(12.5), listing.ServerTime);
        // Both were last active at T0 (the phone's write, the laptop's check-in), so the id breaks the tie.
        Assert.Equal(["laptop", "phone"], listing.Devices.Select(d => d.DeviceId));

        var phone = listing.Devices.Single(d => d.DeviceId == "phone");
        Assert.Equal(("a's phone", "android", T0), (phone.Name, phone.Platform, phone.LastActiveAt));
        var session = phone.Session!;
        Assert.Equal(("song-9", 30d, true, 42.5), (session.SongId, session.PositionSeconds, session.IsPlaying, session.EstimatedPositionSeconds));
        Assert.Equal(["song-1", "song-2"], session.History);
        Assert.Equal(["song-10", "song-11"], session.QueuedNext);
        Assert.True(JsonNode.DeepEquals(JsonSerializer.SerializeToNode(scope), JsonSerializer.SerializeToNode(session.Scope)));
        Assert.Null(session.TakenOverBy);
        Assert.Null(session.Command);
        Assert.Null(listing.Devices.Single(d => d.DeviceId == "laptop").Session);

        Assert.Equal([(user.UserId, "phone", "phone")], _notifier.Signals);
    }

    [Fact]
    public async Task Put_WhilePaused_EstimatesTheStoredPosition_AndAnOmittedScopeAndListsAreEmpty()
    {
        var user = await SeedUserAsync("a", "phone");

        await PutOkAsync(user, "phone", new { songId = "song-1", positionSeconds = 61.25, isPlaying = false });
        _clock.UtcNow = T0.AddMinutes(5);
        var session = (await GetOkAsync(user)).Devices.Single().Session!;

        Assert.Equal(61.25, session.EstimatedPositionSeconds);
        Assert.Null(session.Scope);
        Assert.Empty(session.History);
        Assert.Empty(session.QueuedNext);
    }

    [Fact]
    public async Task Put_OverwritesTheRowInPlace_LastWriteWins()
    {
        var user = await SeedUserAsync("a", "phone");

        await PutOkAsync(user, "phone", State("song-1", 10, true, new { a = 1 }, ["x"], ["y"]));
        _clock.UtcNow = T0.AddSeconds(5);
        await PutOkAsync(user, "phone", State(null, 0, false, null, [], []));

        var row = (await RowAsync(user.UserId, "phone"))!;
        Assert.Equal((null, 0d, false, null, T0.AddSeconds(5)), (row.SongId, row.PositionSeconds, row.IsPlaying, row.Scope, row.UpdatedAt));
        Assert.Empty(row.History);
        Assert.Empty(row.QueuedNext);
    }

    [Fact]
    public async Task Put_ForADeviceThatIsntCheckedIn_Is404_AndWritesNothing()
    {
        var user = await SeedUserAsync("a");

        var response = await PutAsync(user, "never-checked-in", State());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(await RowAsync(user.UserId, "never-checked-in"));
        Assert.Empty(_notifier.Signals);
    }

    [Fact]
    public async Task Get_LeavesOutDevicesInactiveFor30Days_UnlessOnline_AndOrdersByLastActivity()
    {
        var user = await SeedUserAsync("a", "old", "older-but-online", "fresh");
        _clock.UtcNow = T0.AddDays(10);
        await PutOkAsync(user, "fresh", State());
        _presence.Online[user.UserId] = ["older-but-online"];

        _clock.UtcNow = T0.AddDays(31);
        var listing = await GetOkAsync(user);

        Assert.Equal([("fresh", false), ("older-but-online", true)], listing.Devices.Select(d => (d.DeviceId, d.Online)));
        Assert.Equal(T0.AddDays(10), listing.Devices[0].LastActiveAt);
    }

    public static TheoryData<string, object> BadStates => new()
    {
        { " ", State() },
        { new string('d', UserDevice.MaxDeviceIdLength + 1), State() },
        { "phone\u0001", State() },
        { "phone", State(songId: new string('s', Listen.MaxIdLength + 1)) },
        { "phone", State(songId: "song\n1") },
        { "phone", State(position: -1) },
        { "phone", State(scope: new[] { "not", "an", "object" }) },
        { "phone", State(scope: "a string") },
        { "phone", State(scope: new { blob = new string('x', PlaybackSession.MaxScopeLength) }) },
        { "phone", State(history: Enumerable.Range(0, PlaybackSession.MaxHistory + 1).Select(i => $"s{i}").ToArray()) },
        { "phone", State(queuedNext: Enumerable.Range(0, PlaybackSession.MaxQueuedNext + 1).Select(i => $"s{i}").ToArray()) },
        { "phone", State(history: [""]) },
        { "phone", State(queuedNext: ["ok", new string('s', Listen.MaxIdLength + 1)]) },
        { "phone", State(queuedNext: ["bad\u0000id"]) },
    };

    [Theory]
    [MemberData(nameof(BadStates))]
    public async Task Put_WithAnInvalidValue_Is400_AndWritesNothing(string deviceId, object body)
    {
        var user = await SeedUserAsync("a", "phone");

        var response = await PutAsync(user, deviceId, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(await RowAsync(user.UserId, "phone"));
    }

    [Fact]
    public async Task Put_AtTheMaximumSizes_IsAccepted()
    {
        var user = await SeedUserAsync("a", "phone");

        await PutOkAsync(user, "phone", State(
            songId: new string('s', Listen.MaxIdLength),
            history: Enumerable.Range(0, PlaybackSession.MaxHistory).Select(i => $"h{i}").ToArray(),
            queuedNext: Enumerable.Range(0, PlaybackSession.MaxQueuedNext).Select(i => $"q{i}").ToArray()));

        var row = (await RowAsync(user.UserId, "phone"))!;
        Assert.Equal((PlaybackSession.MaxHistory, PlaybackSession.MaxQueuedNext), (row.History.Count, row.QueuedNext.Count));
    }

    [Fact]
    public async Task EveryRoute_WithoutAToken_Is401()
    {
        await SeedUserAsync("a", "phone");
        var anonymous = await ClientAsync(null);

        var responses = new[]
        {
            await anonymous.GetAsync("/api/v1/playback/sessions"),
            await anonymous.PutAsJsonAsync(SessionUrl("phone"), State()),
            await anonymous.PostAsJsonAsync($"{SessionUrl("phone")}/takeover", new { byDeviceId = "x", expectedUpdatedAt = T0 }),
            await anonymous.PostAsJsonAsync($"{SessionUrl("phone")}/command", new { commandId = "c", kind = "pause" }),
            await anonymous.PostAsync($"{SessionUrl("phone")}/command/c/ack", null),
        };

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));
    }

    // --- Takeover -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TakeOver_WithTheUpdatedAtItRead_PausesTheSourceAtItsEstimate_AndRecordsTheTaker()
    {
        var user = await SeedUserAsync("a", "phone", "laptop");
        var read = await PutOkAsync(user, "phone", State("song-1", 30, true));
        _clock.UtcNow = T0.AddSeconds(20);
        _notifier.Signals.Clear();

        var response = await TakeOverAsync(user, "phone", "laptop", read.UpdatedAt);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var source = (await response.Content.ReadFromJsonAsync<PlaybackSessionDto>())!;
        var now = T0.AddSeconds(20);
        Assert.Equal(("song-1", 50d, false, 50d, now, "laptop", (DateTime?)now, now),
            (source.SongId, source.PositionSeconds, source.IsPlaying, source.EstimatedPositionSeconds, source.PositionAt,
             source.TakenOverBy, source.TakenOverAt, source.UpdatedAt));
        Assert.Equal([(user.UserId, "phone", "laptop")], _notifier.Signals);
    }

    [Fact]
    public async Task TakeOver_AgainstAStaleUpdatedAt_Is409_AndChangesNothing()
    {
        var user = await SeedUserAsync("a", "phone", "laptop");
        var read = await PutOkAsync(user, "phone", State("song-1", 30, true));
        // The phone skips to the next song after the laptop read it.
        _clock.UtcNow = T0.AddSeconds(3);
        await PutOkAsync(user, "phone", State("song-2", 0, true));
        _notifier.Signals.Clear();

        var response = await TakeOverAsync(user, "phone", "laptop", read.UpdatedAt);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var row = (await RowAsync(user.UserId, "phone"))!;
        Assert.Equal(("song-2", true, null, T0.AddSeconds(3)), (row.SongId, row.IsPlaying, row.TakenOverBy, row.UpdatedAt));
        Assert.Empty(_notifier.Signals);
    }

    [Fact]
    public async Task TakeOver_AcceptsTheUpdatedAtWithAnOffset_AsTheSameInstant()
    {
        var user = await SeedUserAsync("a", "phone", "laptop");
        var read = await PutOkAsync(user, "phone", State());

        var response = await (await ClientAsync(user)).PostAsync($"{SessionUrl("phone")}/takeover", JsonContent.Create(new
        {
            byDeviceId = "laptop",
            expectedUpdatedAt = new DateTimeOffset(read.UpdatedAt, TimeSpan.Zero).ToOffset(TimeSpan.FromHours(9)),
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Put_AfterATakeover_KeepsTheTakeover_AndHandsItBackToTheSource()
    {
        var user = await SeedUserAsync("a", "phone", "laptop");
        var read = await PutOkAsync(user, "phone", State());
        _clock.UtcNow = T0.AddSeconds(1);
        Assert.Equal(HttpStatusCode.OK, (await TakeOverAsync(user, "phone", "laptop", read.UpdatedAt)).StatusCode);

        // A PUT the phone had in flight lands after the takeover.
        _clock.UtcNow = T0.AddSeconds(2);
        var answer = await PutOkAsync(user, "phone", State("song-1", 31, true));

        Assert.Equal(("laptop", (DateTime?)T0.AddSeconds(1)), (answer.TakenOverBy, answer.TakenOverAt));
    }

    [Fact]
    public async Task TakeOver_WithUpdatedAtReadIntoAMillisecondDate_Lands()
    {
        // A server clock with sub-millisecond ticks, and a client (the app) that parses updatedAt into a JavaScript
        // Date, which keeps only milliseconds, then sends that Date back.
        var user = await SeedUserAsync("a", "phone", "laptop");
        _clock.UtcNow = T0.AddTicks(12_345);
        var response = await PutAsync(user, "phone", State());
        var raw = JsonNode.Parse(await response.Content.ReadAsStringAsync())!["updatedAt"]!.GetValue<string>();
        var asDate = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.Parse(raw).ToUnixTimeMilliseconds()).UtcDateTime;

        Assert.Equal("2026-11-13T12:00:00.001Z", raw);
        Assert.Equal(T0.AddMilliseconds(1), (await RowAsync(user.UserId, "phone"))!.UpdatedAt);
        Assert.Equal(HttpStatusCode.OK, (await TakeOverAsync(user, "phone", "laptop", asDate)).StatusCode);
    }

    [Fact]
    public async Task Put_NamingTheTakeoverItActedOn_ClearsIt_ButAMissingOrStaleOneLeavesIt()
    {
        var user = await SeedUserAsync("a", "phone", "laptop", "tablet");
        var read = await PutOkAsync(user, "phone", State());
        _clock.UtcNow = T0.AddSeconds(1);
        var first = (await (await TakeOverAsync(user, "phone", "laptop", read.UpdatedAt)).Content.ReadFromJsonAsync<PlaybackSessionDto>())!;

        // A PUT that names no takeover (say, one already in flight when the takeover landed) leaves it.
        _clock.UtcNow = T0.AddSeconds(2);
        var plain = await PutOkAsync(user, "phone", State(playing: false));
        Assert.Equal(("laptop", first.TakenOverAt), (plain.TakenOverBy, plain.TakenOverAt));

        // The tablet takes over again before the phone retires the first one, so the phone's echo of the first is stale.
        _clock.UtcNow = T0.AddSeconds(3);
        var second = (await (await TakeOverAsync(user, "phone", "tablet", plain.UpdatedAt)).Content.ReadFromJsonAsync<PlaybackSessionDto>())!;
        _clock.UtcNow = T0.AddSeconds(4);
        var stale = await PutOkAsync(user, "phone", new { songId = "song-1", positionSeconds = 30, isPlaying = false, seenTakenOverAt = first.TakenOverAt });
        Assert.Equal(("tablet", second.TakenOverAt), (stale.TakenOverBy, stale.TakenOverAt));

        // Echoing the current one clears it, in the answer and on the row.
        _clock.UtcNow = T0.AddSeconds(5);
        var retired = await PutOkAsync(user, "phone", new { songId = "song-1", positionSeconds = 30, isPlaying = false, seenTakenOverAt = second.TakenOverAt });
        Assert.Null(retired.TakenOverBy);
        Assert.Null(retired.TakenOverAt);
        var row = (await RowAsync(user.UserId, "phone"))!;
        Assert.Null(row.TakenOverBy);
        Assert.Null(row.TakenOverAt);
    }

    [Fact]
    public async Task TakeOver_OfASessionThatDoesntExist_Is404()
    {
        var user = await SeedUserAsync("a", "phone", "laptop");

        var response = await TakeOverAsync(user, "phone", "laptop", T0);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public static TheoryData<string?, bool> BadTakeOvers => new()
    {
        { "phone", true },     // from itself
        { null, true },        // no taker
        { "laptop\u0001", true },
        { "laptop", false },   // no expectedUpdatedAt
    };

    [Theory]
    [MemberData(nameof(BadTakeOvers))]
    public async Task TakeOver_WithAnInvalidBody_Is400(string? byDeviceId, bool withExpected)
    {
        var user = await SeedUserAsync("a", "phone", "laptop");
        var read = await PutOkAsync(user, "phone", State());

        var response = await TakeOverAsync(user, "phone", byDeviceId, withExpected ? read.UpdatedAt : null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null((await RowAsync(user.UserId, "phone"))!.TakenOverBy);
    }

    [Fact]
    public async Task Put_KeepsNonAsciiScopeTextAsIs_SoItCostsItsRealLength()
    {
        // 3,000 characters of Japanese would be 18,000 as \uXXXX escapes, over the 8,192 cap.
        var user = await SeedUserAsync("a", "phone");
        var tag = string.Concat(Enumerable.Repeat("ロック", 1000));

        var answer = await PutOkAsync(user, "phone", State(scope: new { tags = new[] { tag } }));

        Assert.Equal($"{{\"tags\":[\"{tag}\"]}}", (await RowAsync(user.UserId, "phone"))!.Scope);
        Assert.Equal(tag, answer.Scope!.Value.GetProperty("tags")[0].GetString());
    }

    // --- Commands -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Ack_WithAControlCharacterInTheCommandId_Is400()
    {
        var user = await SeedUserAsync("a", "phone");
        await PutOkAsync(user, "phone", State());

        Assert.Equal(HttpStatusCode.BadRequest, (await AckAsync(user, "phone", "cmd\u0001")).StatusCode);
    }

    [Fact]
    public async Task Command_IsPendingUntilItsOwnAck_AndAWrongAckIs409()
    {
        var user = await SeedUserAsync("a", "phone", "laptop");
        await PutOkAsync(user, "phone", State());
        _clock.UtcNow = T0.AddSeconds(4);
        _notifier.Signals.Clear();

        var sent = await CommandAsync(user, "phone", "cmd-1", PlaybackCommandKinds.Seek, 90);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        Assert.Equal(new PlaybackCommandDto("cmd-1", "seek", 90, T0.AddSeconds(4)),
            (await sent.Content.ReadFromJsonAsync<PlaybackSessionDto>())!.Command);
        Assert.Equal([(user.UserId, "phone", (string?)null)], _notifier.Signals);

        Assert.Equal(HttpStatusCode.Conflict, (await AckAsync(user, "phone", "cmd-other")).StatusCode);
        Assert.Equal("cmd-1", (await RowAsync(user.UserId, "phone"))!.CommandId);

        var acked = await AckAsync(user, "phone", "cmd-1");
        Assert.Equal(HttpStatusCode.OK, acked.StatusCode);
        Assert.Null((await acked.Content.ReadFromJsonAsync<PlaybackSessionDto>())!.Command);
        var row = (await RowAsync(user.UserId, "phone"))!;
        Assert.Equal((null, null, null, null), (row.Command, row.CommandId, row.CommandSeekSeconds, row.CommandAt));
        Assert.Equal((user.UserId, "phone", (string?)"phone"), _notifier.Signals.Last());

        // Acking twice is a conflict, not a second clear.
        Assert.Equal(HttpStatusCode.Conflict, (await AckAsync(user, "phone", "cmd-1")).StatusCode);
    }

    [Fact]
    public async Task Command_ANewerOneReplacesAnUnackedOne_AndTheOldAckCantClearIt()
    {
        var user = await SeedUserAsync("a", "phone");
        await PutOkAsync(user, "phone", State());

        await CommandAsync(user, "phone", "cmd-1", PlaybackCommandKinds.Pause);
        await CommandAsync(user, "phone", "cmd-2", PlaybackCommandKinds.Next);

        Assert.Equal(HttpStatusCode.Conflict, (await AckAsync(user, "phone", "cmd-1")).StatusCode);
        var row = (await RowAsync(user.UserId, "phone"))!;
        Assert.Equal(("cmd-2", "next", (double?)null), (row.CommandId, row.Command, row.CommandSeekSeconds));
    }

    [Fact]
    public async Task Command_DoesntMoveUpdatedAt_SoATakeoverInFlightStillLands()
    {
        var user = await SeedUserAsync("a", "phone", "laptop");
        var read = await PutOkAsync(user, "phone", State());
        _clock.UtcNow = T0.AddSeconds(1);
        await CommandAsync(user, "phone", "cmd-1", PlaybackCommandKinds.Pause);

        Assert.Equal(T0, (await RowAsync(user.UserId, "phone"))!.UpdatedAt);
        Assert.Equal(HttpStatusCode.OK, (await TakeOverAsync(user, "phone", "laptop", read.UpdatedAt)).StatusCode);
    }

    [Fact]
    public async Task Command_OlderThanItsLifetime_IsNoLongerHandedOut_ButItsAckStillClearsIt()
    {
        var user = await SeedUserAsync("a", "phone", "laptop");
        await PutOkAsync(user, "phone", State());
        Assert.Equal(HttpStatusCode.OK, (await CommandAsync(user, "phone", "cmd-1", PlaybackCommandKinds.Play)).StatusCode);

        _clock.UtcNow = T0 + PlaybackSession.CommandLifetime;
        Assert.Equal("cmd-1", (await GetOkAsync(user)).Devices.Single(d => d.DeviceId == "phone").Session!.Command?.CommandId);

        // The phone was asleep; it wakes after the press has gone stale.
        _clock.UtcNow = T0 + PlaybackSession.CommandLifetime + TimeSpan.FromMilliseconds(1);
        Assert.Null((await GetOkAsync(user)).Devices.Single(d => d.DeviceId == "phone").Session!.Command);
        Assert.Null((await PutOkAsync(user, "phone", State())).Command);

        Assert.Equal(HttpStatusCode.OK, (await AckAsync(user, "phone", "cmd-1")).StatusCode);
        Assert.Null((await RowAsync(user.UserId, "phone"))!.CommandId);
    }

    [Fact]
    public async Task Command_ToADeviceWithoutASession_Is404_AndAckIs404()
    {
        var user = await SeedUserAsync("a", "phone");

        Assert.Equal(HttpStatusCode.NotFound, (await CommandAsync(user, "phone", "cmd-1", "pause")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AckAsync(user, "phone", "cmd-1")).StatusCode);
    }

    public static TheoryData<string?, string?, double?> BadCommands => new()
    {
        { "cmd-1", "explode", null },
        { "cmd-1", null, null },
        { "cmd-1", "Pause", null },
        { "cmd-1", "seek", null },
        { "cmd-1", "seek", -1 },
        { "cmd-1", "pause", 10 },
        { null, "pause", null },
        { new string('c', PlaybackSession.MaxCommandIdLength + 1), "pause", null },
        { "cmd\n1", "pause", null },
    };

    [Theory]
    [MemberData(nameof(BadCommands))]
    public async Task Command_WithAnInvalidBody_Is400_AndStoresNothing(string? commandId, string? kind, double? seekSeconds)
    {
        var user = await SeedUserAsync("a", "phone");
        await PutOkAsync(user, "phone", State());

        var response = await CommandAsync(user, "phone", commandId, kind, seekSeconds);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null((await RowAsync(user.UserId, "phone"))!.CommandId);
    }

    // --- Isolation between users ----------------------------------------------------------------------------------

    [Fact]
    public async Task NoRoute_ReadsOrWritesAnotherUsersSessions_OrTargetsAnotherUsersDevice()
    {
        var a = await SeedUserAsync("a", "a-phone");
        var b = await SeedUserAsync("b", "b-phone");
        var aRead = await PutOkAsync(a, "a-phone", State("a-song", 30, true));
        await CommandAsync(a, "a-phone", "a-cmd", PlaybackCommandKinds.Pause);
        var bRead = await PutOkAsync(b, "b-phone", State("b-song", 5, true));
        var aBefore = (await RowAsync(a.UserId, "a-phone"))!;
        _notifier.Signals.Clear();

        // B's listing holds only B's device.
        Assert.Equal(["b-phone"], (await GetOkAsync(b)).Devices.Select(d => d.DeviceId));
        // B can't write, take over, command or ack A's device.
        Assert.Equal(HttpStatusCode.NotFound, (await PutAsync(b, "a-phone", State("b-overwrite"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await TakeOverAsync(b, "a-phone", "b-phone", aRead.UpdatedAt)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await CommandAsync(b, "a-phone", "b-cmd", PlaybackCommandKinds.Next)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AckAsync(b, "a-phone", "a-cmd")).StatusCode);
        // Nor name A's device as the taker of B's own session.
        Assert.Equal(HttpStatusCode.NotFound, (await TakeOverAsync(b, "b-phone", "a-phone", bRead.UpdatedAt)).StatusCode);

        var aAfter = (await RowAsync(a.UserId, "a-phone"))!;
        Assert.Equal((aBefore.SongId, aBefore.UpdatedAt, aBefore.IsPlaying, aBefore.TakenOverBy, aBefore.CommandId),
            (aAfter.SongId, aAfter.UpdatedAt, aAfter.IsPlaying, aAfter.TakenOverBy, aAfter.CommandId));
        Assert.Null((await RowAsync(b.UserId, "b-phone"))!.TakenOverBy);
        Assert.Null(await RowAsync(b.UserId, "a-phone"));
        Assert.Empty(_notifier.Signals);
    }

    [Fact]
    public async Task TheSameDeviceIdUnderTwoUsers_IsTwoSessions_AndEachUserSeesAndSignalsOnlyTheirOwn()
    {
        var a = await SeedUserAsync("a", "shared");
        var b = await SeedUserAsync("b", "shared");

        await PutOkAsync(a, "shared", State("a-song"));
        await PutOkAsync(b, "shared", State("b-song"));

        Assert.Equal("a-song", (await GetOkAsync(a)).Devices.Single().Session!.SongId);
        Assert.Equal("b-song", (await GetOkAsync(b)).Devices.Single().Session!.SongId);
        Assert.Equal([(a.UserId, "shared", "shared"), (b.UserId, "shared", "shared")], _notifier.Signals);
    }

    // --- Prune ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Prune_DeletesSessionsUntouchedFor30Days_AndKeepsTheirDevices()
    {
        var user = await SeedUserAsync("a", "stale", "fresh");
        await PutOkAsync(user, "stale", State());
        _clock.UtcNow = T0.AddDays(5);
        await PutOkAsync(user, "fresh", State());

        var services = new ServiceCollection().AddScoped<ShuffullContext>(_ => _database.CreateContext()).BuildServiceProvider();
        var prune = new PlaybackSessionPruneService(services, _clock, NullLogger<PlaybackSessionPruneService>.Instance);

        Assert.Equal(0, await prune.PruneAsync(T0.AddDays(30), CancellationToken.None));
        Assert.Equal(1, await prune.PruneAsync(T0.AddDays(30).AddTicks(10), CancellationToken.None));

        Assert.Null(await RowAsync(user.UserId, "stale"));
        Assert.NotNull(await RowAsync(user.UserId, "fresh"));
        await using var context = _database.CreateContext();
        Assert.Equal(2, await context.UserDevices.CountAsync());
    }

    private sealed class RecordingPlaybackNotifier : IPlaybackNotifier
    {
        public List<(string UserId, string DeviceId, string? WriterDeviceId)> Signals { get; } = [];

        public Task SessionChangedAsync(string userId, string deviceId, string? writerDeviceId)
        {
            lock (Signals)
            {
                Signals.Add((userId, deviceId, writerDeviceId));
            }
            return Task.CompletedTask;
        }
    }

    private sealed class FakePlaybackPresence : IPlaybackPresence
    {
        public Dictionary<string, HashSet<string>> Online { get; } = [];

        public IReadOnlySet<string> OnlineDeviceIds(string userId) => Online.GetValueOrDefault(userId) ?? [];
    }
}
