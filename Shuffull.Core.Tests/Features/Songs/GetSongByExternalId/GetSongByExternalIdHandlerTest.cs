using Nut.Results;
using Shuffull.Core.Features.Songs.GetSongByExternalId;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Tests.Infrastructure;
using Shuffull.Shared.Tools;

namespace Shuffull.Core.Tests.Features.Songs.GetSongByExternalId;

/// <summary>
/// The producer's video-id -> SongId bridge for replace-in-place. Covers the hit shape (id, name, artists,
/// curator flags) and the miss, which the controller turns into a 404 so the funnel can tell "gone" apart
/// from "broken".
/// </summary>
public class GetSongByExternalIdHandlerTest : IDisposable
{
    private readonly DatabaseFixture _database;
    private readonly GetSongByExternalIdHandler _handler;

    public GetSongByExternalIdHandlerTest()
    {
        _database = new DatabaseFixture();
        _handler = new GetSongByExternalIdHandler(_database.Context);
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<Song> SeedAsync(string? externalId, params string[] artists)
    {
        var song = new Song
        {
            SongId = IdGenerator.Generate(),
            Name = $"song-{IdGenerator.Generate()[..6]}",
            FileExtension = ".mp3",
            FileHash = IdGenerator.Generate(),
            ExternalSongId = externalId,
            MetadataLocked = true,
            Exploratory = false,
            Version = DateTime.UtcNow,
        };
        _database.Context.Songs.Add(song);
        foreach (var name in artists)
        {
            var artist = new Artist { ArtistId = IdGenerator.Generate(), Name = name };
            _database.Context.Artists.Add(artist);
            _database.Context.SongArtists.Add(new SongArtist
            {
                SongArtistId = IdGenerator.Generate(), SongId = song.SongId, ArtistId = artist.ArtistId,
            });
        }
        await _database.Context.SaveChangesAsync();
        return song;
    }

    [Fact]
    public async Task Handle_KnownExternalId_ReturnsSongIdAndArtists()
    {
        var song = await SeedAsync("yt-abc", "Artist A", "Artist B");
        await SeedAsync("yt-other", "Someone Else");

        var result = await _handler.Handle(new GetSongByExternalIdQuery("yt-abc"), CancellationToken.None);

        Assert.True(result.IsOk);
        var response = result.Get();
        Assert.Equal(song.SongId, response.SongId);
        Assert.Equal(song.Name, response.Name);
        Assert.Equal("yt-abc", response.ExternalSongId);
        Assert.Equal(["Artist A", "Artist B"], response.Artists.OrderBy(a => a));
        Assert.True(response.MetadataLocked);
        Assert.False(response.Exploratory);
    }

    [Fact]
    public async Task Handle_UnknownExternalId_ReturnsError()
    {
        await SeedAsync("yt-abc");

        var result = await _handler.Handle(new GetSongByExternalIdQuery("yt-missing"), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("yt-missing", result.GetError().Message);
    }

    [Fact]
    public async Task Handle_ManualUploadWithNullExternalId_IsNotMatchedByEmptyLookup()
    {
        // A manual upload has no external id; nothing should resolve to it.
        await SeedAsync(externalId: null);

        var result = await _handler.Handle(new GetSongByExternalIdQuery(""), CancellationToken.None);

        Assert.True(result.IsError);
    }
}
