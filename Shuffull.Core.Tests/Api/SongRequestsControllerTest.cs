using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Shuffull.Api.Tools;
using Microsoft.EntityFrameworkCore;
using Shuffull.Api.Commands.SongRequests;
using Shuffull.Api.Controllers;
using Shuffull.Core.Features.SongRequests;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Models.Enums;
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

    public void Dispose()
    {
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

    private SongRequestsController Controller(User? user = null)
    {
        var controller = new SongRequestsController(_database.CreateContext(), new ManualTimeProvider(Now))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        if (user is not null)
        {
            controller.HttpContext.Items["User"] = user;
        }
        return controller;
    }

    private static CreateSongRequestRequest Ask(string externalSongId = "yt-1") =>
        new(externalSongId, "Everybody Wants You", ["Billy Squier"], 230, "https://example.test/thumb.jpg");

    private async Task<SongRequestDto> CreateOkAsync(User user, string externalSongId = "yt-1") =>
        Assert.IsType<SongRequestDto>(Assert.IsType<OkObjectResult>(await Controller(user).Create(Ask(externalSongId), CancellationToken.None)).Value);

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
        var queue = Assert.IsType<OkObjectResult>(await Controller().GetQueue(SongRequestStatus.Pending, CancellationToken.None));
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
            await Controller().GetQueue(SongRequestStatus.Pending, CancellationToken.None)).Value);
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

        var conflict = Assert.IsType<ConflictObjectResult>(await Controller(user).Create(Ask("yt-new"), CancellationToken.None));

        Assert.Equal(first.SongRequestId, Assert.IsType<SongRequestDto>(conflict.Value).SongRequestId);
        Assert.Equal(1, await _database.CreateContext().SongRequests.CountAsync());

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
        for (var i = 0; i < SongRequestsController.MaxOpenPerUser; i++)
        {
            await CreateOkAsync(user, $"yt-{i}");
        }

        var refused = Assert.IsType<ObjectResult>(await Controller(user).Create(Ask("yt-one-more"), CancellationToken.None));
        Assert.Equal(StatusCodes.Status429TooManyRequests, refused.StatusCode);
    }

    [Fact]
    public async Task Create_WithoutATitle_IsABadRequest()
    {
        var user = await SeedUserAsync("a");

        Assert.IsType<BadRequestObjectResult>(await Controller(user).Create(Ask() with { Title = " " }, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await Controller(user).Create(Ask() with { Artists = null }, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await Controller(user).Create(Ask(new string('x', 65)), CancellationToken.None));
    }

    [Fact]
    public async Task Cancel_APendingRequest_CancelsIt_ButNotOneTheProducerClaimed_OrAnotherUsers()
    {
        var user = await SeedUserAsync("a");
        var other = await SeedUserAsync("b");
        var pending = await CreateOkAsync(user, "yt-1");
        var claimed = await CreateOkAsync(user, "yt-2");
        await AckAsync(new(claimed.SongRequestId, SongRequestAckStatus.Accepted));

        Assert.IsType<NotFoundObjectResult>(await Controller(other).Cancel(pending.SongRequestId, CancellationToken.None));
        Assert.Equal(SongRequestStatus.Pending, (await ReloadAsync(pending.SongRequestId)).Status);

        Assert.IsType<NoContentResult>(await Controller(user).Cancel(pending.SongRequestId, CancellationToken.None));
        Assert.Equal(SongRequestStatus.Cancelled, (await ReloadAsync(pending.SongRequestId)).Status);

        Assert.IsType<ConflictObjectResult>(await Controller(user).Cancel(claimed.SongRequestId, CancellationToken.None));
        Assert.Equal(SongRequestStatus.Accepted, (await ReloadAsync(claimed.SongRequestId)).Status);
    }

    [Fact]
    public async Task GetMine_ListsOnlyTheCallersRequests()
    {
        var user = await SeedUserAsync("a");
        var other = await SeedUserAsync("b");
        var mine = await CreateOkAsync(user, "yt-1");
        await CreateOkAsync(other, "yt-2");

        var list = Assert.IsType<List<SongRequestDto>>(Assert.IsType<OkObjectResult>(await Controller(user).GetMine(CancellationToken.None)).Value);

        Assert.Equal(mine.SongRequestId, Assert.Single(list).SongRequestId);
    }

    [Theory]
    [InlineData("", nameof(SongRequestsController.GetMine))]
    [InlineData("?status=Pending", nameof(SongRequestsController.GetQueue))]
    [InlineData("?status=0", nameof(SongRequestsController.GetQueue))]
    public void Get_WithAStatus_IsTheProducersQueue_AndWithoutOne_TheUsersList(string query, string expected)
    {
        var http = new DefaultHttpContext();
        http.Request.QueryString = new QueryString(query);
        var route = new RouteContext(http);

        var chosen = new[] { nameof(SongRequestsController.GetMine), nameof(SongRequestsController.GetQueue) }
            .Where(name => typeof(SongRequestsController).GetMethod(name)!
                .GetCustomAttributes(typeof(QueryParameterAttribute), false).Cast<QueryParameterAttribute>()
                .All(selector => selector.IsValidForRequest(route, new ActionDescriptor())));

        Assert.Equal(expected, Assert.Single(chosen));
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
