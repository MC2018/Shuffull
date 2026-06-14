using Nut.Results;
using Shuffull.Core.Tests.Infrastructure;
using Shuffull.Core.Features.Songs.GetSong;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Tests.Features.Songs.GetSong;

public class GetSongHandlerTest : IDisposable
{
    private readonly DatabaseFixture _database;
    private readonly GetSongHandler _handler;

    public GetSongHandlerTest()
    {
        _database = new DatabaseFixture();
        _handler = new GetSongHandler(_database.UnitOfWork);
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
    public async Task Handle_WithValidSongId_ReturnsSong()
    {
        // Arrange
        var song = NewSong(Guid.NewGuid().ToString(), "My Song");
        await _database.Context.Songs.AddAsync(song);
        await _database.Context.SaveChangesAsync();

        // Act
        var result = await _handler.Handle(new GetSongQuery(song.SongId), CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        var dto = result.Get().Song;
        Assert.Equal(song.SongId, dto.SongId);
        Assert.Equal("My Song", dto.Name);
        Assert.Equal(".mp3", dto.FileExtension);
        Assert.Empty(dto.Artists);
        Assert.Empty(dto.Tags);
    }

    [Fact]
    public async Task Handle_WithNonExistentSong_ReturnsNotFoundError()
    {
        // Act
        var result = await _handler.Handle(new GetSongQuery(Guid.NewGuid().ToString()), CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        Assert.Contains("not found", result.GetError().Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_WithArtistsAndTags_PopulatesCollections()
    {
        // Arrange — a song with one artist and one genre tag, wired through the join entities so
        // the spec's Includes and the DTO's collection mapping are both exercised.
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
        var result = await _handler.Handle(new GetSongQuery(song.SongId), CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        var dto = result.Get().Song;
        Assert.Single(dto.Artists);
        Assert.Contains("Test Artist", dto.Artists);
        Assert.Single(dto.Tags);
        Assert.Contains("Rock", dto.Tags);
    }
}
