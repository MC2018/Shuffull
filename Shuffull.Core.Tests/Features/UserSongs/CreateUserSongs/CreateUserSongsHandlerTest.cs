using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Features.UserSongs.CreateUserSongs;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Features.UserSongs.CreateUserSongs;

public class CreateUserSongsHandlerTest : IDisposable
{
    private readonly DatabaseFixture _database;
    private readonly CreateUserSongsHandler _handler;

    public CreateUserSongsHandlerTest()
    {
        _database = new DatabaseFixture();
        _handler = new CreateUserSongsHandler(_database.UnitOfWork);
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<User> SeedUserAsync()
    {
        var user = new User
        {
            UserId = Guid.NewGuid().ToString(),
            Username = $"user-{Guid.NewGuid():N}",
            Version = DateTime.UtcNow,
            ServerHash = "server-hash",
        };
        await _database.Context.Users.AddAsync(user);
        await _database.Context.SaveChangesAsync();
        return user;
    }

    private async Task<Song> SeedSongAsync()
    {
        var song = new Song
        {
            SongId = Guid.NewGuid().ToString(),
            Name = "Song",
            FileExtension = ".mp3",
            FileHash = Guid.NewGuid().ToString(),
            ExternalSongId = null,
        };
        await _database.Context.Songs.AddAsync(song);
        await _database.Context.SaveChangesAsync();
        return song;
    }

    [Fact]
    public async Task Handle_WithExistingSongs_CreatesUserSongs()
    {
        // Arrange
        var user = await SeedUserAsync();
        var song1 = await SeedSongAsync();
        var song2 = await SeedSongAsync();

        // Act
        var result = await _handler.Handle(
            new CreateUserSongsCommand(user.UserId, new[] { song1.SongId, song2.SongId }),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        Assert.Equal(2, result.Get().Created.Count);

        var saved = await _database.Context.UserSongs.AsNoTracking()
            .CountAsync(us => us.UserId == user.UserId);
        Assert.Equal(2, saved);
    }

    [Fact]
    public async Task Handle_WithUnknownSongIds_SkipsThem()
    {
        // Arrange
        var user = await SeedUserAsync();
        var song = await SeedSongAsync();

        // Act — one real song, one id that matches no song.
        var result = await _handler.Handle(
            new CreateUserSongsCommand(user.UserId, new[] { song.SongId, Guid.NewGuid().ToString() }),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        Assert.Single(result.Get().Created);
        Assert.Equal(song.SongId, result.Get().Created[0].SongId);
    }

    [Fact]
    public async Task Handle_WhenUserAlreadyHasSong_SkipsDuplicates()
    {
        // Arrange
        var user = await SeedUserAsync();
        var song1 = await SeedSongAsync();
        var song2 = await SeedSongAsync();
        await _database.Context.UserSongs.AddAsync(new UserSong
        {
            UserId = user.UserId,
            SongId = song1.SongId,
            LastPlayed = DateTime.UtcNow,
            Version = DateTime.UtcNow,
        });
        await _database.Context.SaveChangesAsync();

        // Act
        var result = await _handler.Handle(
            new CreateUserSongsCommand(user.UserId, new[] { song1.SongId, song2.SongId }),
            CancellationToken.None);

        // Assert — only song2 is new.
        Assert.True(result.IsOk);
        Assert.Single(result.Get().Created);
        Assert.Equal(song2.SongId, result.Get().Created[0].SongId);

        var total = await _database.Context.UserSongs.AsNoTracking()
            .CountAsync(us => us.UserId == user.UserId);
        Assert.Equal(2, total);
    }
}
