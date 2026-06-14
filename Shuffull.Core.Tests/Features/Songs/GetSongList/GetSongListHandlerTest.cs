using Nut.Results;
using Shuffull.Core.Tests.Infrastructure;
using Shuffull.Core.Features.Songs.GetSongList;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Tests.Features.Songs.GetSongList;

public class GetSongListHandlerTest : IDisposable
{
    private readonly DatabaseFixture _database;
    private readonly GetSongListHandler _handler;

    public GetSongListHandlerTest()
    {
        _database = new DatabaseFixture();
        _handler = new GetSongListHandler(_database.UnitOfWork);
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private static Song NewSong(string id, string name = "Test Song")
        => new()
        {
            SongId = id,
            Name = name,
            FileExtension = ".mp3",
            FileHash = $"hash-{id}",
            ExternalSongId = null,
        };

    [Fact]
    public async Task Handle_WithMatchingIds_ReturnsOnlyRequestedSongs()
    {
        // Arrange — three songs exist, but only two are requested.
        var song1 = NewSong(Guid.NewGuid().ToString(), "Song One");
        var song2 = NewSong(Guid.NewGuid().ToString(), "Song Two");
        var song3 = NewSong(Guid.NewGuid().ToString(), "Song Three");
        await _database.Context.Songs.AddRangeAsync(song1, song2, song3);
        await _database.Context.SaveChangesAsync();

        // Act
        var result = await _handler.Handle(
            new GetSongListQuery(new[] { song1.SongId, song2.SongId }),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        var songs = result.Get().Songs;
        Assert.Equal(2, songs.Count);
        Assert.Contains(songs, s => s.SongId == song1.SongId);
        Assert.Contains(songs, s => s.SongId == song2.SongId);
        Assert.DoesNotContain(songs, s => s.SongId == song3.SongId);
    }

    [Fact]
    public async Task Handle_WithNoMatches_ReturnsEmptyList()
    {
        // Act — request ids that don't exist in the database.
        var result = await _handler.Handle(
            new GetSongListQuery(new[] { Guid.NewGuid().ToString() }),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        Assert.Empty(result.Get().Songs);
    }

    [Fact]
    public async Task Handle_PopulatesArtistsAndTags()
    {
        // Arrange — one requested song wired to an artist and a genre tag through the join entities.
        var song = NewSong(Guid.NewGuid().ToString());
        var artist = new Artist { ArtistId = Guid.NewGuid().ToString(), Name = "Test Artist" };
        var genre = new Genre { TagId = Guid.NewGuid().ToString(), Name = "Rock" };

        await _database.Context.Songs.AddAsync(song);
        await _database.Context.Artists.AddAsync(artist);
        await _database.Context.Tags.AddAsync(genre);
        await _database.Context.SongArtists.AddAsync(new SongArtist
        {
            SongArtistId = Guid.NewGuid().ToString(),
            SongId = song.SongId,
            ArtistId = artist.ArtistId,
            Song = song,
            Artist = artist,
        });
        await _database.Context.SongTags.AddAsync(new SongTag
        {
            SongTagId = Guid.NewGuid().ToString(),
            SongId = song.SongId,
            TagId = genre.TagId,
            Song = song,
            Tag = genre,
        });
        await _database.Context.SaveChangesAsync();

        // Act
        var result = await _handler.Handle(
            new GetSongListQuery(new[] { song.SongId }),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        var dto = Assert.Single(result.Get().Songs);
        Assert.Contains("Test Artist", dto.Artists);
        Assert.Contains("Rock", dto.Tags);
    }
}
