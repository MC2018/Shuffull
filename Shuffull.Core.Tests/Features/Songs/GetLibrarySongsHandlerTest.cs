using Nut.Results;
using Shuffull.Core.Features.Songs.GetLibrarySongs;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Features.Songs;

/// <summary>
/// The producer's whole-library listing that feeds the funnel's duplicate registry. Covers the payload (identity,
/// artists, media location), keyset paging that neither skips nor repeats, and that exploratory songs are listed
/// too — an audition is still a song an alternate upload can duplicate.
/// </summary>
public class GetLibrarySongsHandlerTest : IDisposable
{
    private readonly DatabaseFixture _database;

    public GetLibrarySongsHandlerTest() => _database = new DatabaseFixture();

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<Song> SeedAsync(string songId, string name, string? externalId = null, bool exploratory = false, params string[] artists)
    {
        var song = new Song
        {
            SongId = songId,
            Name = name,
            FileExtension = ".mp3",
            FileHash = $"hash-{songId}",
            ExternalSongId = externalId,
            Exploratory = exploratory,
            Version = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        _database.Context.Songs.Add(song);
        foreach (var artistName in artists)
        {
            var artist = new Artist { ArtistId = $"artist-{songId}-{artistName}", Name = artistName };
            _database.Context.Add(artist);
            _database.Context.Add(new SongArtist { SongArtistId = $"sa-{songId}-{artistName}", SongId = songId, ArtistId = artist.ArtistId });
        }

        await _database.Context.SaveChangesAsync();
        return song;
    }

    private Task<Result<LibrarySongsPage>> RunAsync(string? after = null, int limit = 500)
        => new GetLibrarySongsHandler(_database.Context).Handle(new GetLibrarySongsQuery(after, limit), CancellationToken.None);

    [Fact]
    public async Task Handle_ReturnsIdentityArtistsAndMediaLocation()
    {
        await SeedAsync("01a", "Some Song", "yt-01a", false, "Some Artist");

        var page = (await RunAsync()).Get();

        var song = Assert.Single(page.Songs);
        Assert.Equal("01a", song.SongId);
        Assert.Equal("yt-01a", song.ExternalSongId);
        Assert.Equal("Some Song", song.Name);
        Assert.Equal(["Some Artist"], song.Artists);
        Assert.Equal("hash-01a", song.FileHash);
        Assert.Equal(".mp3", song.FileExtension);
        Assert.Null(page.NextAfterSongId);
    }

    [Fact]
    public async Task Handle_ListsExploratoryAndManualSongsToo()
    {
        await SeedAsync("01a", "Kept song", "yt-1");
        await SeedAsync("01b", "Audition", "yt-2", exploratory: true);
        await SeedAsync("01c", "Manual upload", externalId: null);

        var page = (await RunAsync()).Get();

        Assert.Equal(["01a", "01b", "01c"], page.Songs.Select(s => s.SongId));
    }

    [Fact]
    public async Task Handle_KeysetPages_CoverEverySongExactlyOnce()
    {
        // Inserted out of order so the test proves ordering comes from the key, not insertion.
        foreach (var id in new[] { "01e", "01a", "01d", "01b", "01c" })
        {
            await SeedAsync(id, $"song {id}");
        }

        var seen = new List<string>();
        string? after = null;
        var pages = 0;
        do
        {
            var page = (await RunAsync(after, limit: 2)).Get();
            seen.AddRange(page.Songs.Select(s => s.SongId));
            after = page.NextAfterSongId;
            pages++;
        }
        while (after != null && pages < 10);

        Assert.Equal(["01a", "01b", "01c", "01d", "01e"], seen);
        Assert.Equal(3, pages);
    }

    [Fact]
    public async Task Handle_ExactMultipleOfLimit_EndsWithoutAnEmptyExtraPage()
    {
        await SeedAsync("01a", "a");
        await SeedAsync("01b", "b");

        var page = (await RunAsync(limit: 2)).Get();

        Assert.Equal(2, page.Songs.Count);
        Assert.Null(page.NextAfterSongId);
    }

    [Fact]
    public async Task Handle_NonPositiveLimit_FallsBackToDefault()
    {
        await SeedAsync("01a", "a");

        var page = (await RunAsync(limit: 0)).Get();

        Assert.Single(page.Songs);
    }
}
