using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Api;

/// <summary>
/// The replacement queue over HTTP: the app flags a poor-quality song (signed in), the funnel pulls the queue (shared
/// secret). A flag is global per song, so a second flag while one is open adds nothing.
/// </summary>
public class ReplacementsEndpointTest : IDisposable
{
    private readonly DatabaseFixture _database = new();
    private readonly RecordingHealthSignals _signals = new();
    private readonly User _user = new() { UserId = "user-1", Username = "max" };

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<Song> SeedSongAsync(string id, string? externalId = "video-1", params string[] artists)
    {
        var song = new Song
        {
            SongId = id,
            Name = $"Name of {id}",
            FileExtension = ".mp3",
            FileHash = $"hash-{id}",
            ExternalSongId = externalId,
            Version = DateTime.UtcNow,
        };
        _database.Context.Songs.Add(song);
        foreach (var artist in artists)
        {
            var row = new Artist { ArtistId = $"{id}-{artist}", Name = artist };
            _database.Context.Artists.Add(row);
            _database.Context.SongArtists.Add(new SongArtist { SongId = id, ArtistId = row.ArtistId });
        }
        await _database.Context.SaveChangesAsync();
        return song;
    }

    private async Task SeedReplacementAsync(string id, string songId, SongReplacementStatus status, DateTime createdAt,
        string? note = null, string? original = null)
    {
        _database.Context.SongReplacements.Add(new SongReplacement
        {
            SongReplacementId = id,
            SongId = songId,
            Status = status,
            Note = note,
            OriginalExternalSongId = original,
            CreatedAt = createdAt,
        });
        await _database.Context.SaveChangesAsync();
    }

    private Task<ApiTestHost> StartAsync(bool signedIn = true) =>
        ProducerEndpointsHost.StartAsync(_database, _signals, signedIn ? _user : null);

    private static async Task<HttpResponseMessage> FlagAsync(ApiTestHost host, string json) =>
        await host.Client.PostAsync("api/v1/replacements", new StringContent(json, Encoding.UTF8, "application/json"));

    private async Task<List<SongReplacement>> ReplacementsAsync() =>
        await _database.CreateContext().SongReplacements.AsNoTracking().OrderBy(r => r.CreatedAt).ToListAsync();

    // ── Flag ────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Flag_NoToken_Is401WithTheFilterBody()
    {
        await SeedSongAsync("song-1");
        await using var host = await StartAsync(signedIn: false);

        var response = await FlagAsync(host, """{"songId":"song-1"}""");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("""{"message":"Unauthorized."}""", await response.Content.ReadAsStringAsync());
        Assert.Empty(await ReplacementsAsync());
    }

    [Fact]
    public async Task Flag_ASong_QueuesItPending_WithTheSourceIdSnapshotAndTheNote()
    {
        await SeedSongAsync("song-1", "video-1");
        await using var host = await StartAsync();
        var before = DateTime.UtcNow;

        var response = await FlagAsync(host, """{"songId":"song-1","note":"clipped intro"}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync());
        var row = Assert.Single(await ReplacementsAsync());
        Assert.Equal("song-1", row.SongId);
        Assert.Equal(SongReplacementStatus.Pending, row.Status);
        Assert.Equal("clipped intro", row.Note);
        Assert.Equal("video-1", row.OriginalExternalSongId);
        Assert.Null(row.ResolvedAt);
        Assert.False(string.IsNullOrWhiteSpace(row.SongReplacementId));
        Assert.True(row.CreatedAt >= before.AddSeconds(-1), $"CreatedAt {row.CreatedAt:o}");
    }

    [Fact]
    public async Task Flag_NoNote_AndNoSourceId_AreStoredAsNull()
    {
        await SeedSongAsync("song-1", externalId: null);
        await using var host = await StartAsync();

        var response = await FlagAsync(host, """{"songId":"song-1"}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = Assert.Single(await ReplacementsAsync());
        Assert.Null(row.Note);
        Assert.Null(row.OriginalExternalSongId);
    }

    [Theory]
    [InlineData(SongReplacementStatus.Pending)]
    [InlineData(SongReplacementStatus.InProgress)]
    public async Task Flag_WhileOneIsOpen_AddsNothing(SongReplacementStatus open)
    {
        await SeedSongAsync("song-1");
        await SeedReplacementAsync("existing", "song-1", open, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), "first");
        await using var host = await StartAsync();

        var response = await FlagAsync(host, """{"songId":"song-1","note":"second"}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = Assert.Single(await ReplacementsAsync());
        Assert.Equal("existing", row.SongReplacementId);
        Assert.Equal("first", row.Note);
    }

    [Fact]
    public async Task Flag_AfterAnEarlierOneCompleted_QueuesANewOne()
    {
        await SeedSongAsync("song-1");
        await SeedReplacementAsync("done", "song-1", SongReplacementStatus.Completed, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await using var host = await StartAsync();

        var response = await FlagAsync(host, """{"songId":"song-1"}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rows = await ReplacementsAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(SongReplacementStatus.Pending, rows[1].Status);
    }

    [Fact]
    public async Task Flag_AnUnknownSong_Is404WithAPlainMessage()
    {
        await using var host = await StartAsync();

        var response = await FlagAsync(host, """{"songId":"no-such-song"}""");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.StartsWith("text/plain", response.Content.Headers.ContentType?.ToString());
        Assert.Equal("Song not found.", await response.Content.ReadAsStringAsync());
        Assert.Empty(await ReplacementsAsync());
    }

    [Theory]
    [InlineData("""{"songId":"   "}""")]
    [InlineData("""{"songId":""}""")]
    public async Task Flag_ABlankSongId_Is400WithAPlainMessage(string json)
    {
        await using var host = await StartAsync();

        var response = await FlagAsync(host, json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.StartsWith("text/plain", response.Content.Headers.ContentType?.ToString());
        Assert.Equal("SongId is required.", await response.Content.ReadAsStringAsync());
        Assert.Empty(await ReplacementsAsync());
    }

    [Theory]
    [InlineData("""{"note":"x"}""", "SongId")]
    [InlineData("""{"songId":null}""", "SongId")]
    [InlineData("null", "request")]
    public async Task Flag_NoSongIdAtAll_IsABindingError(string json, string field)
    {
        await using var host = await StartAsync();

        var response = await FlagAsync(host, json);

        await ProducerEndpointsHost.AssertFrameworkValidation400Async(response, field);
        Assert.Empty(await ReplacementsAsync());
    }

    // ── Queue ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Queue_ReturnsRowsAtThatStatus_OldestFirst_InTheWireShape()
    {
        await SeedSongAsync("song-1", "video-1", "Artist A");
        await SeedSongAsync("song-2", "video-2", "Artist B");
        await SeedSongAsync("song-3", "video-3");
        await SeedReplacementAsync("r-newer", "song-1", SongReplacementStatus.Pending,
            new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc), "bad audio", "video-old");
        await SeedReplacementAsync("r-older", "song-2", SongReplacementStatus.Pending,
            new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc));
        await SeedReplacementAsync("r-done", "song-3", SongReplacementStatus.Completed,
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
        await using var host = await StartAsync(signedIn: false);

        var response = await host.Client.SendAsync(ProducerEndpointsHost.Producer(HttpMethod.Get, "api/v1/replacements?status=0"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            """[{"songReplacementId":"r-older","songId":"song-2","name":"Name of song-2","artists":["Artist B"],"originalExternalSongId":null,"note":null,"createdAt":"2026-02-01T12:00:00"},""" +
            """{"songReplacementId":"r-newer","songId":"song-1","name":"Name of song-1","artists":["Artist A"],"originalExternalSongId":"video-old","note":"bad audio","createdAt":"2026-03-01T12:00:00"}]""",
            await response.Content.ReadAsStringAsync());
        Assert.Empty(_signals.Successes);
    }

    [Fact]
    public async Task Queue_ListsEveryArtist_AndAnUndefinedStatusIsABindingError()
    {
        await SeedSongAsync("song-1", "video-1", "Artist A", "Artist B");
        await SeedReplacementAsync("r-1", "song-1", SongReplacementStatus.InProgress, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await using var host = await StartAsync(signedIn: false);

        var byName = await host.Client.SendAsync(ProducerEndpointsHost.Producer(HttpMethod.Get, "api/v1/replacements?status=InProgress"));
        var undefined = await host.Client.SendAsync(ProducerEndpointsHost.Producer(HttpMethod.Get, "api/v1/replacements?status=99"));

        var body = await byName.Content.ReadAsStringAsync();
        Assert.Contains("\"Artist A\"", body);
        Assert.Contains("\"Artist B\"", body);
        await ProducerEndpointsHost.AssertFrameworkValidation400Async(undefined, "status");
    }

    [Fact]
    public async Task Queue_ASignedInAppUserWithoutTheKey_Is401()
    {
        await using var host = await StartAsync(signedIn: true);

        var response = await host.Client.GetAsync("api/v1/replacements?status=0");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
