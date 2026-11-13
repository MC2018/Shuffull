using System.Net;
using System.Net.Http.Json;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Nut.Results;
using Shuffull.Api.Commands.SongRequests;
using Shuffull.Api.Controllers;
using Shuffull.Api.Tools.Authorization;
using Shuffull.Core.Behaviors;
using Shuffull.Core.Features.SongRequests;
using Shuffull.Core.Features.SongRequests.CreateSongRequest;
using Shuffull.Core.Features.SongRequests.GetMySongRequests;
using Shuffull.Core.Features.SongRequests.GetSongRequestQueue;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Persistence;
using Shuffull.Core.Services.Alerts;
using Shuffull.Core.Tests.Infrastructure;
using Shuffull.Metadata.Contracts;
using Shuffull.Metadata.Enums;
using static Shuffull.Core.Tests.Infrastructure.SongImportHarness;

namespace Shuffull.Core.Tests.Api;

/// <summary>
/// Song requests (Shuffull#57): a known song links at once, an unknown one waits for the producer, whose acks only move
/// a request from a state they may follow, and whose import completes it.
/// </summary>
public class SongRequestsControllerTest : IDisposable
{
    // A Wednesday, so the week's requests playlist is named for Monday 2026-11-02.
    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
    private const string WeekPlaylist = "Requests · week of 2026-11-02";
    private readonly DatabaseFixture _database = new();
    private readonly ServiceProvider _provider;
    private readonly List<IServiceScope> _scopes = [];

    public SongRequestsControllerTest()
    {
        // The real pipeline, as in Program.cs: validators run before each handler.
        var coreAssembly = typeof(ValidationBehavior<,>).Assembly;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => _database.CreateContext());
        services.AddSingleton<TimeProvider>(new ManualTimeProvider(Now));
        services.AddValidatorsFromAssemblies([coreAssembly]);
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblies(coreAssembly);
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });
        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _scopes.ForEach(scope => scope.Dispose());
        _provider.Dispose();
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<User> SeedUserAsync(string name)
    {
        var user = new User { UserId = $"user-{name}", Username = name, Version = Now, ServerHash = "server-hash" };
        _database.Context.Users.Add(user);
        await _database.Context.SaveChangesAsync();
        return user;
    }

    private async Task<Song> SeedSongAsync(string externalSongId)
    {
        var song = new Song
        {
            SongId = $"song-{externalSongId}",
            Name = "Everybody Wants You",
            FileExtension = ".mp3",
            FileHash = Guid.NewGuid().ToString("N"),
            ExternalSongId = externalSongId,
            Version = Now,
        };
        _database.Context.Songs.Add(song);
        await _database.Context.SaveChangesAsync();
        return song;
    }

    private SongRequestsController Controller()
    {
        var scope = _provider.CreateScope();
        _scopes.Add(scope);
        return new SongRequestsController(scope.ServiceProvider.GetRequiredService<IMediator>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider },
            },
        };
    }

    private static CreateSongRequestRequest Ask(string externalSongId = "yt-1") =>
        new(externalSongId, "Everybody Wants You", ["Billy Squier"], 230, "https://example.test/thumb.jpg");

    private async Task<SongRequestDto> CreateOkAsync(User user, string externalSongId = "yt-1") =>
        Assert.IsType<SongRequestDto>(Assert.IsType<OkObjectResult>(await Controller().Create(user, Ask(externalSongId), CancellationToken.None)).Value);

    private async Task<SongRequestAckResponse> AckAsync(SongRequestAckDto ack, params SongRequestAckDto[] more) =>
        Assert.IsType<SongRequestAckResponse>(Assert.IsType<OkObjectResult>(await Controller().Ack([ack, .. more], CancellationToken.None)).Value);

    private async Task<SongRequest> ReloadAsync(string id) =>
        await _database.CreateContext().SongRequests.AsNoTracking().SingleAsync(r => r.SongRequestId == id);

    private async Task SetStatusAsync(string id, SongRequestStatus status, string? reason = null)
    {
        using var context = _database.CreateContext();
        await context.SongRequests.Where(r => r.SongRequestId == id)
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.Status, status).SetProperty(r => r.FailureReason, reason));
    }

    [Fact]
    public async Task Create_ForASongTheLibraryHas_CompletesAtOnce_AndLinksItToTheWeeksRequestsPlaylist()
    {
        var user = await SeedUserAsync("a");
        var song = await SeedSongAsync("yt-1");

        var dto = await CreateOkAsync(user);

        Assert.Equal(SongRequestStatus.Completed, dto.Status);
        Assert.Equal(song.SongId, dto.SongId);
        Assert.Equal(WeekPlaylist, dto.TargetPlaylistName);

        using var read = _database.CreateContext();
        Assert.True(await read.UserSongs.AnyAsync(us => us.UserId == user.UserId && us.SongId == song.SongId));
        var playlist = await read.Playlists.AsNoTracking().SingleAsync(p => p.UserId == user.UserId && p.Name == WeekPlaylist);
        Assert.True(playlist.IsExploratory);
        Assert.True(await read.PlaylistSongs.AnyAsync(ps => ps.PlaylistId == playlist.PlaylistId && ps.SongId == song.SongId));
        Assert.Equal(SongRequestStatus.Completed, (await ReloadAsync(dto.SongRequestId)).Status);

        // Nothing for the producer to do.
        var queue = Assert.IsType<OkObjectResult>(await Controller().GetQueue(new RecordingHealthSignals(), SongRequestStatus.Pending, CancellationToken.None));
        Assert.Empty(Assert.IsType<List<PendingSongRequestDto>>(queue.Value));
    }

    [Fact]
    public async Task Create_ForANewSong_IsPending_ThenTheAckMovesIt_AndItsImportCompletesIt()
    {
        var user = await SeedUserAsync("a");

        var dto = await CreateOkAsync(user, "yt-new");
        Assert.Equal(SongRequestStatus.Pending, dto.Status);
        Assert.Null(dto.SongId);

        var queue = Assert.IsType<List<PendingSongRequestDto>>(Assert.IsType<OkObjectResult>(
            await Controller().GetQueue(new RecordingHealthSignals(), SongRequestStatus.Pending, CancellationToken.None)).Value);
        var pending = Assert.Single(queue);
        // Records compare a List by reference, so the artists are checked on their own.
        List<string> noArtists = [];
        Assert.Equal(new PendingSongRequestDto(dto.SongRequestId, user.UserId, "yt-new", "Everybody Wants You",
            noArtists, 230, "https://example.test/thumb.jpg", WeekPlaylist), pending with { Artists = noArtists });
        Assert.Equal(["Billy Squier"], pending.Artists);

        Assert.Equal([dto.SongRequestId], (await AckAsync(new(dto.SongRequestId, SongRequestAckStatus.Accepted))).Applied);
        Assert.Equal([dto.SongRequestId], (await AckAsync(new(dto.SongRequestId, SongRequestAckStatus.Downloading))).Applied);
        Assert.Equal(SongRequestStatus.Downloading, (await ReloadAsync(dto.SongRequestId)).Status);

        var import = await RunImportAsync(_database, new SongImport
        {
            SongImportId = Guid.NewGuid().ToString(),
            Name = "Everybody Wants You",
            ImportFolder = "folder",
            FileType = ".mp3",
            UserId = user.UserId,
            ExternalSongId = "yt-new",
            TargetPlaylistName = pending.TargetPlaylistName,
            Exploratory = true,
            SongRequestId = dto.SongRequestId,
            LastUpdatedAt = DateTime.UtcNow,
        }, Mp3Frames());

        Assert.Equal(SongImportState.Completed, import.State);
        var done = await ReloadAsync(dto.SongRequestId);
        Assert.Equal(SongRequestStatus.Completed, done.Status);
        Assert.Equal(import.SongId, done.SongId);
        using var read = _database.CreateContext();
        var playlist = await read.Playlists.AsNoTracking().SingleAsync(p => p.UserId == user.UserId && p.Name == WeekPlaylist);
        Assert.True(await read.PlaylistSongs.AnyAsync(ps => ps.PlaylistId == playlist.PlaylistId && ps.SongId == import.SongId));

        // A late ack can't reopen it.
        Assert.Equal([dto.SongRequestId], (await AckAsync(new(dto.SongRequestId, SongRequestAckStatus.Failed, "late"))).Rejected);
        Assert.Equal(SongRequestStatus.Completed, (await ReloadAsync(dto.SongRequestId)).Status);
    }

    [Fact]
    public async Task Import_OfACancelledRequest_LinksTheSong_ButLeavesTheRequestCancelled()
    {
        var user = await SeedUserAsync("a");
        var dto = await CreateOkAsync(user, "yt-new");
        await SetStatusAsync(dto.SongRequestId, SongRequestStatus.Cancelled);

        var import = await RunImportAsync(_database, new SongImport
        {
            SongImportId = Guid.NewGuid().ToString(),
            Name = "Everybody Wants You",
            ImportFolder = "folder",
            FileType = ".mp3",
            UserId = user.UserId,
            ExternalSongId = "yt-new",
            TargetPlaylistName = WeekPlaylist,
            Exploratory = true,
            SongRequestId = dto.SongRequestId,
            LastUpdatedAt = DateTime.UtcNow,
        }, Mp3Frames());

        Assert.Equal(SongImportState.Completed, import.State);
        var saved = await ReloadAsync(dto.SongRequestId);
        Assert.Equal(SongRequestStatus.Cancelled, saved.Status);
        Assert.Null(saved.SongId);
    }

    [Fact]
    public async Task Ack_FromAStateItMayNotFollow_IsRejected_AndLeavesTheRequestAlone()
    {
        var user = await SeedUserAsync("a");
        var dto = await CreateOkAsync(user, "yt-new");

        // Downloading before the producer claimed it (Accepted) is stale.
        Assert.Equal([dto.SongRequestId], (await AckAsync(new(dto.SongRequestId, SongRequestAckStatus.Downloading))).Rejected);
        Assert.Equal(SongRequestStatus.Pending, (await ReloadAsync(dto.SongRequestId)).Status);

        // The user cancelled before the producer claimed it: its Accepted comes back rejected, so it drops the download.
        await SetStatusAsync(dto.SongRequestId, SongRequestStatus.Cancelled);
        var response = await AckAsync(new(dto.SongRequestId, SongRequestAckStatus.Accepted));
        Assert.Empty(response.Applied);
        Assert.Equal([dto.SongRequestId], response.Rejected);
        Assert.Equal(SongRequestStatus.Cancelled, (await ReloadAsync(dto.SongRequestId)).Status);
    }

    [Fact]
    public async Task Ack_UnknownOrUndefinedStatus_IsRejected_AndAnUnknownIdToo()
    {
        var user = await SeedUserAsync("a");
        var dto = await CreateOkAsync(user, "yt-new");

        var response = await AckAsync(
            new(dto.SongRequestId, SongRequestAckStatus.Unknown),
            new SongRequestAckDto("no-such-request", SongRequestAckStatus.Accepted));
        Assert.Empty(response.Applied);
        Assert.Equal([dto.SongRequestId, "no-such-request"], response.Rejected);

        Assert.Equal([dto.SongRequestId], (await AckAsync(new(dto.SongRequestId, (SongRequestAckStatus)99))).Rejected);
        Assert.Equal(SongRequestStatus.Pending, (await ReloadAsync(dto.SongRequestId)).Status);
    }

    [Fact]
    public async Task Ack_ReSent_IsApplied_AndAFailureKeepsItsReason()
    {
        var user = await SeedUserAsync("a");
        var dto = await CreateOkAsync(user, "yt-new");

        Assert.Equal([dto.SongRequestId], (await AckAsync(new(dto.SongRequestId, SongRequestAckStatus.Accepted))).Applied);
        Assert.Equal([dto.SongRequestId], (await AckAsync(new(dto.SongRequestId, SongRequestAckStatus.Accepted))).Applied);

        Assert.Equal([dto.SongRequestId], (await AckAsync(new(dto.SongRequestId, SongRequestAckStatus.Failed, "Video unavailable"))).Applied);
        var failed = await ReloadAsync(dto.SongRequestId);
        Assert.Equal(SongRequestStatus.Failed, failed.Status);
        Assert.Equal("Video unavailable", failed.FailureReason);
    }

    [Fact]
    public async Task Create_WhileARequestForTheSongIsOpen_IsRefused()
    {
        var user = await SeedUserAsync("a");
        var first = await CreateOkAsync(user, "yt-new");

        Assert.IsType<ConflictObjectResult>(await Controller().Create(user, Ask("yt-new"), CancellationToken.None));

        Assert.Equal(first.SongRequestId, (await _database.CreateContext().SongRequests.SingleAsync()).SongRequestId);

        // Another user asking for the same song is a separate request.
        var other = await SeedUserAsync("b");
        Assert.NotEqual(first.SongRequestId, (await CreateOkAsync(other, "yt-new")).SongRequestId);
    }

    [Fact]
    public async Task OpenRequestIndex_RefusesASecondOpenRow_ButNotOneAfterTheFirstFinished()
    {
        var user = await SeedUserAsync("a");
        var first = await CreateOkAsync(user, "yt-new");

        using (var context = _database.CreateContext())
        {
            context.SongRequests.Add(new SongRequest
            {
                SongRequestId = "second", UserId = user.UserId, ExternalSongId = "yt-new", Title = "t",
                TargetPlaylistName = WeekPlaylist, CreatedAt = Now, UpdatedAt = Now,
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }

        await SetStatusAsync(first.SongRequestId, SongRequestStatus.Cancelled);
        Assert.Equal(SongRequestStatus.Pending, (await CreateOkAsync(user, "yt-new")).Status);
    }

    [Fact]
    public async Task WeeklyPlaylistIndex_AllowsOneRequestsPlaylistPerUserAndWeek_ButLeavesOtherNamesAlone()
    {
        var user = await SeedUserAsync("a");
        var other = await SeedUserAsync("b");
        Playlist NewPlaylist(string id, string userId, string name) => new()
        {
            PlaylistId = id, UserId = userId, Name = name, PercentUntilReplayable = 0.9m, Version = Now,
        };

        using (var context = _database.CreateContext())
        {
            context.Playlists.AddRange(NewPlaylist("p1", user.UserId, WeekPlaylist), NewPlaylist("p2", other.UserId, WeekPlaylist),
                NewPlaylist("p3", user.UserId, "Mix"), NewPlaylist("p4", user.UserId, "Mix"));
            await context.SaveChangesAsync();
        }

        using var racing = _database.CreateContext();
        racing.Playlists.Add(NewPlaylist("p5", user.UserId, WeekPlaylist));
        await Assert.ThrowsAsync<DbUpdateException>(() => racing.SaveChangesAsync());
    }

    [Fact]
    public async Task Create_ForAFailedRequest_PutsTheSameRequestBackOnTheQueue()
    {
        var user = await SeedUserAsync("a");
        var first = await CreateOkAsync(user, "yt-new");
        await SetStatusAsync(first.SongRequestId, SongRequestStatus.Failed, "Video unavailable");

        var retried = await CreateOkAsync(user, "yt-new");

        Assert.Equal(first.SongRequestId, retried.SongRequestId);
        Assert.Equal(SongRequestStatus.Pending, retried.Status);
        Assert.Null((await ReloadAsync(first.SongRequestId)).FailureReason);
    }

    [Fact]
    public async Task Create_WhenAnOpenRequestsSongHasSinceArrived_CompletesThatRequest()
    {
        var user = await SeedUserAsync("a");
        var first = await CreateOkAsync(user, "yt-1");
        var song = await SeedSongAsync("yt-1");

        var again = await CreateOkAsync(user, "yt-1");

        Assert.Equal(first.SongRequestId, again.SongRequestId);
        Assert.Equal(SongRequestStatus.Completed, again.Status);
        Assert.Equal(song.SongId, again.SongId);
        Assert.True(await _database.CreateContext().UserSongs.AnyAsync(us => us.UserId == user.UserId && us.SongId == song.SongId));
    }

    [Fact]
    public async Task Create_PastTheOpenRequestLimit_IsRefused()
    {
        var user = await SeedUserAsync("a");
        for (var i = 0; i < CreateSongRequestHandler.MaxOpenPerUser; i++)
        {
            await CreateOkAsync(user, $"yt-{i}");
        }

        Assert.IsType<ConflictObjectResult>(await Controller().Create(user, Ask("yt-one-more"), CancellationToken.None));
        Assert.Equal(CreateSongRequestHandler.MaxOpenPerUser, await _database.CreateContext().SongRequests.CountAsync());
    }

    public static TheoryData<string, CreateSongRequestRequest> BadRequests => new()
    {
        { "blank title", Ask() with { Title = " " } },
        { "no artists list", Ask() with { Artists = null } },
        { "a blank artist", Ask() with { Artists = ["Billy Squier", " "] } },
        { "a negative duration", Ask() with { DurationSeconds = -1 } },
        { "an id too long", Ask(new string('x', 65)) },
        { "an id with a space", Ask("yt 1") },
        { "an id that is an option", Ask("--exec=rm") },
        { "an id that is a url", Ask("https://www.youtube.com/watch?v=dQw4w9WgXcQ") },
        { "an http thumbnail", Ask() with { ThumbnailUrl = "http://example.test/thumb.jpg" } },
        { "a relative thumbnail", Ask() with { ThumbnailUrl = "/thumb.jpg" } },
        { "a script thumbnail", Ask() with { ThumbnailUrl = "javascript:alert(1)" } },
    };

    [Theory]
    [MemberData(nameof(BadRequests))]
    public async Task Create_WithABadField_IsABadRequest_AndWritesNothing(string name, CreateSongRequestRequest request)
    {
        var user = await SeedUserAsync("a");

        var result = await Controller().Create(user, request, CancellationToken.None);

        Assert.True(result is BadRequestObjectResult, $"{name}: got {result.GetType().Name}");
        Assert.Equal(0, await _database.CreateContext().SongRequests.CountAsync());
    }

    [Fact]
    public async Task Create_WithoutAThumbnail_IsFine()
    {
        var user = await SeedUserAsync("a");

        var dto = Assert.IsType<SongRequestDto>(Assert.IsType<OkObjectResult>(
            await Controller().Create(user, Ask() with { ThumbnailUrl = null, Artists = [] }, CancellationToken.None)).Value);

        Assert.Null(dto.ThumbnailUrl);
        Assert.Empty(dto.Artists);
    }

    [Fact]
    public async Task Cancel_APendingRequest_CancelsIt_ButNotOneTheProducerClaimed_AFailedOne_OrAnotherUsers()
    {
        var user = await SeedUserAsync("a");
        var other = await SeedUserAsync("b");
        var pending = await CreateOkAsync(user, "yt-1");
        var claimed = await CreateOkAsync(user, "yt-2");
        await AckAsync(new(claimed.SongRequestId, SongRequestAckStatus.Accepted));
        var failed = await CreateOkAsync(user, "yt-3");
        await SetStatusAsync(failed.SongRequestId, SongRequestStatus.Failed, "Video unavailable");

        Assert.IsType<NotFoundObjectResult>(await Controller().Cancel(other, pending.SongRequestId, CancellationToken.None));
        Assert.Equal(SongRequestStatus.Pending, (await ReloadAsync(pending.SongRequestId)).Status);

        var cancelled = Assert.IsType<SongRequestDto>(Assert.IsType<OkObjectResult>(
            await Controller().Cancel(user, pending.SongRequestId, CancellationToken.None)).Value);
        Assert.Equal(SongRequestStatus.Cancelled, cancelled.Status);
        Assert.Equal(SongRequestStatus.Cancelled, (await ReloadAsync(pending.SongRequestId)).Status);

        Assert.IsType<ConflictObjectResult>(await Controller().Cancel(user, claimed.SongRequestId, CancellationToken.None));
        Assert.Equal(SongRequestStatus.Accepted, (await ReloadAsync(claimed.SongRequestId)).Status);

        // A Failed request may still be completed by a producer that keeps trying, so it isn't cancelled either.
        Assert.IsType<ConflictObjectResult>(await Controller().Cancel(user, failed.SongRequestId, CancellationToken.None));
        Assert.Equal(SongRequestStatus.Failed, (await ReloadAsync(failed.SongRequestId)).Status);
    }

    [Fact]
    public async Task GetMine_ListsOnlyTheCallersRequests()
    {
        var user = await SeedUserAsync("a");
        var other = await SeedUserAsync("b");
        var mine = await CreateOkAsync(user, "yt-1");
        await CreateOkAsync(other, "yt-2");

        var list = Assert.IsType<List<SongRequestDto>>(Assert.IsType<OkObjectResult>(await Controller().GetMine(user, CancellationToken.None)).Value);

        Assert.Equal(mine.SongRequestId, Assert.Single(list).SongRequestId);
    }

    private static Task<ApiTestHost> StartHostAsync(User? signedInAs) => ApiTestHost.StartAsync(services =>
    {
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [ProducerKeyAttribute.ConfigKey] = "import-key" }).Build());
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetMySongRequestsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok<IReadOnlyList<SongRequestDto>>([]));
        mediator.Setup(m => m.Send(It.IsAny<GetSongRequestQueueQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok<IReadOnlyList<PendingSongRequestDto>>([]));
        services.AddSingleton(mediator.Object);
        services.AddSingleton<IHealthSignals>(new RecordingHealthSignals());
    }, signedInAs);

    [Fact]
    public async Task Get_WithoutAStatus_IsTheUsersList_AndWithOne_TheProducersQueue()
    {
        var user = new User { UserId = "user-a", Username = "a", Version = Now, ServerHash = "server-hash" };
        await using var signedIn = await StartHostAsync(user);
        await using var anonymous = await StartHostAsync(signedInAs: null);

        // Signed in, no status: the user's own list.
        Assert.Equal(HttpStatusCode.OK, (await signedIn.Client.GetAsync("api/v1/song-requests")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.Client.GetAsync("api/v1/song-requests")).StatusCode);

        // With a status it is the producer's queue, which a signed-in user without the key can't read.
        Assert.Equal(HttpStatusCode.Unauthorized, (await signedIn.Client.GetAsync("api/v1/song-requests?status=Pending")).StatusCode);
        var producer = new HttpRequestMessage(HttpMethod.Get, "api/v1/song-requests?status=Pending");
        producer.Headers.Add(ProducerKeyAttribute.HeaderName, "import-key");
        Assert.Equal(HttpStatusCode.OK, (await anonymous.Client.SendAsync(producer)).StatusCode);
    }

    [Fact]
    public async Task Create_OverHttp_BindsTheAppsBody()
    {
        var user = new User { UserId = "user-a", Username = "a", Version = Now, ServerHash = "server-hash" };
        CreateSongRequestCommand? sent = null;
        await using var host = await ApiTestHost.StartAsync(services =>
        {
            var mediator = new Mock<IMediator>();
            mediator.Setup(m => m.Send(It.IsAny<CreateSongRequestCommand>(), It.IsAny<CancellationToken>()))
                .Callback<IRequest<Result<SongRequestDto>>, CancellationToken>((command, _) => sent = (CreateSongRequestCommand)command)
                .ReturnsAsync(Result.Error<SongRequestDto>("stop here"));
            services.AddSingleton(mediator.Object);
        }, user);

        var response = await host.Client.PostAsJsonAsync("api/v1/song-requests", Ask());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(sent);
        Assert.Equal(("user-a", "yt-1", "Everybody Wants You", 230), (sent.UserId, sent.ExternalSongId, sent.Title, sent.DurationSeconds));
        Assert.Equal(["Billy Squier"], sent.Artists!);
    }

    [Theory]
    [InlineData("2026-11-02T00:00:00Z", "2026-11-02")] // Monday
    [InlineData("2026-11-08T23:59:59Z", "2026-11-02")] // Sunday
    [InlineData("2026-11-09T00:00:00Z", "2026-11-09")]
    [InlineData("2027-01-01T08:00:00Z", "2026-12-28")] // across a year
    public void PlaylistName_IsNamedForTheWeeksMondayInUtc(string utcNow, string monday)
    {
        var now = DateTime.Parse(utcNow, null, System.Globalization.DateTimeStyles.AdjustToUniversal);

        Assert.Equal($"Requests · week of {monday}", SongRequestRules.PlaylistName(now));
    }
}
