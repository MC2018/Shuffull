using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Features.UserSongs.UpdateSongsLastPlayed;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Features.UserSongs.UpdateSongsLastPlayed;

public class UpdateSongsLastPlayedHandlerTest : IDisposable
{
    private readonly DatabaseFixture _database;
    private readonly UpdateSongsLastPlayedHandler _handler;

    public UpdateSongsLastPlayedHandlerTest()
    {
        _database = new DatabaseFixture();
        _handler = new UpdateSongsLastPlayedHandler(_database.UnitOfWork);
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<User> SeedUserAsync(DateTime version)
    {
        var user = new User
        {
            UserId = Guid.NewGuid().ToString(),
            Username = $"user-{Guid.NewGuid():N}",
            Version = version,
            ServerHash = "server-hash",
        };
        await _database.Context.Users.AddAsync(user);
        await _database.Context.SaveChangesAsync();
        return user;
    }

    private async Task<UserSong> SeedUserSongAsync(string userId, DateTime lastPlayed)
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

        var userSong = new UserSong
        {
            UserId = userId,
            SongId = song.SongId,
            LastPlayed = lastPlayed,
            Version = lastPlayed,
        };
        await _database.Context.UserSongs.AddAsync(userSong);
        await _database.Context.SaveChangesAsync();
        return userSong;
    }

    [Fact]
    public async Task Handle_WithNewerTimestamp_AdvancesLastPlayedAndBumpsVersions()
    {
        // Arrange
        var oldVersion = DateTime.UtcNow.AddDays(-10);
        var user = await SeedUserAsync(oldVersion);
        var oldLastPlayed = DateTime.UtcNow.AddDays(-5);
        var userSong = await SeedUserSongAsync(user.UserId, oldLastPlayed);
        var newLastPlayed = DateTime.UtcNow;

        // Act
        var result = await _handler.Handle(
            new UpdateSongsLastPlayedCommand(user.UserId, new[] { new SongLastPlayed(userSong.SongId, newLastPlayed) }),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        Assert.Equal(1, result.Get().UpdatedCount);

        var savedUserSong = await _database.Context.UserSongs.AsNoTracking()
            .SingleAsync(us => us.UserId == user.UserId && us.SongId == userSong.SongId);
        Assert.Equal(newLastPlayed, savedUserSong.LastPlayed);
        Assert.True(savedUserSong.Version > oldLastPlayed);

        var savedUser = await _database.Context.Users.AsNoTracking().SingleAsync(u => u.UserId == user.UserId);
        Assert.True(savedUser.Version > oldVersion);
    }

    [Fact]
    public async Task Handle_WithStaleTimestamp_LeavesRecordUntouched()
    {
        // Arrange
        var user = await SeedUserAsync(DateTime.UtcNow.AddDays(-10));
        var lastPlayed = DateTime.UtcNow;
        var userSong = await SeedUserSongAsync(user.UserId, lastPlayed);
        var stale = lastPlayed.AddDays(-3);

        // Act
        var result = await _handler.Handle(
            new UpdateSongsLastPlayedCommand(user.UserId, new[] { new SongLastPlayed(userSong.SongId, stale) }),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        Assert.Equal(0, result.Get().UpdatedCount);

        var savedUserSong = await _database.Context.UserSongs.AsNoTracking()
            .SingleAsync(us => us.UserId == user.UserId && us.SongId == userSong.SongId);
        Assert.Equal(lastPlayed, savedUserSong.LastPlayed);
    }

    [Fact]
    public async Task Handle_WhenNoMatchingUserSong_ReturnsError()
    {
        // Arrange
        var user = await SeedUserAsync(DateTime.UtcNow);

        // Act
        var result = await _handler.Handle(
            new UpdateSongsLastPlayedCommand(user.UserId, new[] { new SongLastPlayed(Guid.NewGuid().ToString(), DateTime.UtcNow) }),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        Assert.Contains("No matching data", result.GetError().Message, StringComparison.OrdinalIgnoreCase);
    }
}
