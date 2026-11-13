using System.Net;
using Microsoft.EntityFrameworkCore;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Api;

/// <summary>
/// The funnel's lyrics delivery over HTTP. It refuses an all-empty payload (so a producer bug can't blank lyrics the
/// site holds), leaves a curator-locked song alone, and bumps <c>Song.Version</c> so the apps pick the lyrics up.
/// </summary>
public class SongLyricsEndpointTest : IDisposable
{
    private static readonly DateTime OldVersion = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly DatabaseFixture _database = new();
    private readonly RecordingHealthSignals _signals = new();

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task SeedSongAsync(bool locked = false)
    {
        _database.Context.Songs.Add(new Song
        {
            SongId = "song-1",
            Name = "Song",
            FileExtension = ".mp3",
            FileHash = "hash-1",
            ExternalSongId = "video-1",
            SyncedLyrics = "[00:01.00] old",
            PlainLyrics = "old",
            LyricsSource = "old-source",
            MetadataLocked = locked,
            Version = OldVersion,
        });
        await _database.Context.SaveChangesAsync();
    }

    private async Task<(HttpStatusCode Status, string Body)> PutAsync(string json)
    {
        await using var host = await ProducerEndpointsHost.StartAsync(_database, _signals);
        var response = await host.Client.SendAsync(ProducerEndpointsHost.Producer(HttpMethod.Put, "api/v1/song-lyrics", json));
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private async Task<Song> ReloadAsync() =>
        await _database.CreateContext().Songs.AsNoTracking().SingleAsync(s => s.SongId == "song-1");

    [Fact]
    public async Task Lyrics_ForAKnownVideo_AreStored_AndTheSongVersionBumps()
    {
        await SeedSongAsync();

        var (status, body) = await PutAsync(
            """{"externalSongId":"video-1","syncedLyrics":"[00:02.00] new","plainLyrics":"new","instrumental":false,"source":"  lrclib  "}""");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("""{"updated":true,"songId":"song-1"}""", body);
        var song = await ReloadAsync();
        Assert.Equal("[00:02.00] new", song.SyncedLyrics);
        Assert.Equal("new", song.PlainLyrics);
        Assert.False(song.LyricsInstrumental);
        Assert.Equal("lrclib", song.LyricsSource);
        Assert.Equal(ProducerEndpointsHost.Now, song.Version);
    }

    [Fact]
    public async Task Lyrics_BlankFields_AreStoredAsNull()
    {
        await SeedSongAsync();

        var (status, _) = await PutAsync("""{"externalSongId":"video-1","syncedLyrics":"  ","plainLyrics":"only plain","instrumental":false,"source":" "}""");

        Assert.Equal(HttpStatusCode.OK, status);
        var song = await ReloadAsync();
        Assert.Null(song.SyncedLyrics);
        Assert.Equal("only plain", song.PlainLyrics);
        Assert.Null(song.LyricsSource);
    }

    [Fact]
    public async Task Lyrics_InstrumentalOnly_ClearsTheText()
    {
        await SeedSongAsync();

        var (status, body) = await PutAsync("""{"externalSongId":"video-1","instrumental":true}""");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("""{"updated":true,"songId":"song-1"}""", body);
        var song = await ReloadAsync();
        Assert.Null(song.SyncedLyrics);
        Assert.Null(song.PlainLyrics);
        Assert.True(song.LyricsInstrumental);
        Assert.Null(song.LyricsSource);
    }

    [Fact]
    public async Task Lyrics_ForALockedSong_AreRefusedWith200_AndChangeNothing()
    {
        await SeedSongAsync(locked: true);

        var (status, body) = await PutAsync("""{"externalSongId":"video-1","plainLyrics":"new","instrumental":false}""");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("""{"updated":false,"reason":"metadata locked"}""", body);
        var song = await ReloadAsync();
        Assert.Equal("old", song.PlainLyrics);
        Assert.Equal("old-source", song.LyricsSource);
        Assert.Equal(OldVersion, song.Version);
    }

    [Fact]
    public async Task Lyrics_ForAnUnknownVideo_Is404()
    {
        await SeedSongAsync();

        var (status, body) = await PutAsync("""{"externalSongId":"no-such-video","plainLyrics":"la","instrumental":false}""");

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal("""{"error":"No song found for external id 'no-such-video'."}""", body);
    }

    [Theory]
    [InlineData("""{"externalSongId":"video-1","instrumental":false}""")]
    [InlineData("""{"externalSongId":"video-1","syncedLyrics":" ","plainLyrics":"","instrumental":false}""")]
    public async Task Lyrics_WithNoContent_Are400_AndChangeNothing(string json)
    {
        await SeedSongAsync();

        var (status, body) = await PutAsync(json);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("""{"error":"No lyrics content supplied; nothing to update."}""", body);
        Assert.Equal("old", (await ReloadAsync()).PlainLyrics);
    }

    [Theory]
    [InlineData("""{"externalSongId":"   ","plainLyrics":"la","instrumental":false}""")]
    [InlineData("""{"externalSongId":"","instrumental":false}""")]
    public async Task Lyrics_WithABlankExternalId_Are400(string json)
    {
        var (status, body) = await PutAsync(json);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("""{"error":"ExternalSongId is required."}""", body);
    }

    [Fact]
    public async Task Lyrics_WithNoExternalIdAtAll_AreABindingError()
    {
        await using var host = await ProducerEndpointsHost.StartAsync(_database, _signals);

        var response = await host.Client.SendAsync(ProducerEndpointsHost.Producer(HttpMethod.Put, "api/v1/song-lyrics",
            """{"plainLyrics":"la","instrumental":false}"""));

        await ProducerEndpointsHost.AssertFrameworkValidation400Async(response, "ExternalSongId");
    }
}
