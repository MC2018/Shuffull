using System.Text.Json;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Nut.Results;
using Shuffull.Core.Features.Songs.GetSongsChanged;
using Shuffull.Core.Features.UserSongs.GetUserSongs;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Services.Alerts;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Api;

/// <summary>
/// The keyset cursor only works if the <c>version</c> a page returns comes back through <c>afterDate</c> unchanged:
/// a cursor even one microsecond early matches the tie again, one late skips it (Shuffull#64). These run the real
/// controllers, so they check what JSON writes and what model binding reads, which a handler test can't.
/// </summary>
public class SyncCursorBindingTest
{
    // Microsecond precision, as Postgres stores it — finer than a JS Date, which is what lost it before.
    private static readonly DateTime Version = new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc).AddTicks(1234560);

    private readonly Mock<IMediator> _mediator = new();

    private Task<ApiTestHost> StartAsync(User? signedInAs = null) => ApiTestHost.StartAsync(services =>
    {
        services.AddSingleton(_mediator.Object);
        services.AddSingleton<IHealthSignals>(new RecordingHealthSignals());
    }, signedInAs);

    private static async Task<JsonElement> ReadLastRowAsync(HttpResponseMessage response, string listName)
    {
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var rows = json.RootElement.GetProperty(listName);
        return rows[rows.GetArrayLength() - 1].Clone();
    }

    [Fact]
    public async Task SongsChanged_ReturnedVersionAndId_BindBackAsTheExactCursor()
    {
        var queries = new List<GetSongsChangedQuery>();
        var song = new Song { SongId = "song-0499", Name = "Song", FileExtension = ".mp3", FileHash = "h", Version = Version };
        _mediator
            .Setup(m => m.Send(It.IsAny<GetSongsChangedQuery>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<Result<GetSongsChangedResponse>>, CancellationToken>((query, _) => queries.Add((GetSongsChangedQuery)query))
            .ReturnsAsync(Result.Ok(new GetSongsChangedResponse(new[] { SongDto.Create(song).Get() }, false)));
        await using var host = await StartAsync(signedInAs: new User { UserId = "u", Username = "alice" });

        var last = await ReadLastRowAsync(await host.Client.GetAsync("/api/v1/songs/changed?afterDate=0001-01-01T00:00:00Z"), "songs");
        var version = last.GetProperty("version").GetString();
        var songId = last.GetProperty("songId").GetString();
        await host.Client.GetAsync($"/api/v1/songs/changed?afterDate={Uri.EscapeDataString(version!)}&afterId={Uri.EscapeDataString(songId!)}");

        Assert.Equal(2, queries.Count);
        Assert.Null(queries[0].AfterId);
        Assert.Equal(Version, queries[1].AfterDate);
        Assert.Equal(Version.Ticks, queries[1].AfterDate.Ticks);
        Assert.Equal(DateTimeKind.Utc, queries[1].AfterDate.Kind);
        Assert.Equal("song-0499", queries[1].AfterId);
    }

    [Fact]
    public async Task UserSongs_ReturnedVersionAndSongId_BindBackAsTheExactCursor()
    {
        var queries = new List<GetUserSongsQuery>();
        var record = new UserSong { UserId = "u", SongId = "song-0499", LastPlayed = Version, Version = Version };
        _mediator
            .Setup(m => m.Send(It.IsAny<GetUserSongsQuery>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<Result<GetUserSongsResponse>>, CancellationToken>((query, _) => queries.Add((GetUserSongsQuery)query))
            .ReturnsAsync(Result.Ok(new GetUserSongsResponse(new[] { UserSongDto.Create(record).Get() }, false)));
        await using var host = await StartAsync(signedInAs: new User { UserId = "u", Username = "alice" });

        var last = await ReadLastRowAsync(await host.Client.GetAsync("/api/v1/user-songs?afterDate=0001-01-01T00:00:00Z"), "userSongs");
        var version = last.GetProperty("version").GetString();
        var songId = last.GetProperty("songId").GetString();
        await host.Client.GetAsync($"/api/v1/user-songs?afterDate={Uri.EscapeDataString(version!)}&afterId={Uri.EscapeDataString(songId!)}");

        Assert.Equal(2, queries.Count);
        Assert.Null(queries[0].AfterId);
        Assert.Equal(Version.Ticks, queries[1].AfterDate.Ticks);
        Assert.Equal(DateTimeKind.Utc, queries[1].AfterDate.Kind);
        Assert.Equal("song-0499", queries[1].AfterId);
        Assert.Equal("u", queries[1].UserId);
    }
}
