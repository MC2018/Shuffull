using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Features.UserSongs.UpdateSongsLastPlayed;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;
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
        var song = await SeedSongAsync();

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

    private async Task<Song> SeedSongAsync()
    {
        var song = new Song
        {
            SongId = Guid.NewGuid().ToString(),
            Name = "Song",
            FileExtension = ".mp3",
            FileHash = Guid.NewGuid().ToString(),
        };
        await _database.Context.Songs.AddAsync(song);
        await _database.Context.SaveChangesAsync();
        return song;
    }

    [Fact]
    public async Task Handle_WhenSongHasNoUserSong_CreatesItWithThePlay()
    {
        // A first play creates the play record (Shuffull#2), so no PUT /user-songs has to come first.
        var oldVersion = DateTime.UtcNow.AddDays(-10);
        var user = await SeedUserAsync(oldVersion);
        var song = await SeedSongAsync();
        var played = DateTime.UtcNow.AddMinutes(-1);

        var result = await _handler.Handle(
            new UpdateSongsLastPlayedCommand(user.UserId, new[]
            {
                new SongLastPlayed(song.SongId, played.AddMinutes(-5)),
                new SongLastPlayed(song.SongId, played),
            }),
            CancellationToken.None);

        Assert.True(result.IsOk, result.IsError ? result.GetError().Message : null);
        Assert.Equal(1, result.Get().UpdatedCount);

        var saved = await _database.Context.UserSongs.AsNoTracking()
            .SingleAsync(us => us.UserId == user.UserId && us.SongId == song.SongId);
        Assert.Equal(played, saved.LastPlayed);
        Assert.Equal(LikeStatus.Neutral, saved.LikeStatus);
        Assert.Null(saved.KeptAt);
        Assert.True(saved.Version > oldVersion);

        // The user's version moves too, so the app's next sync pulls the new row.
        var savedUser = await _database.Context.Users.AsNoTracking().SingleAsync(u => u.UserId == user.UserId);
        Assert.True(savedUser.Version > oldVersion);
    }

    [Fact]
    public async Task Handle_MixedBatch_UpdatesExistingAndCreatesMissingTogether()
    {
        var user = await SeedUserAsync(DateTime.UtcNow.AddDays(-10));
        var existing = await SeedUserSongAsync(user.UserId, DateTime.UtcNow.AddDays(-5));
        var fresh = await SeedSongAsync();
        var played = DateTime.UtcNow;

        var result = await _handler.Handle(
            new UpdateSongsLastPlayedCommand(user.UserId, new[]
            {
                new SongLastPlayed(existing.SongId, played),
                new SongLastPlayed(fresh.SongId, played),
                new SongLastPlayed(Guid.NewGuid().ToString(), played),
            }),
            CancellationToken.None);

        Assert.True(result.IsOk, result.IsError ? result.GetError().Message : null);
        Assert.Equal(2, result.Get().UpdatedCount);
        Assert.Equal(
            new[] { played, played },
            await _database.Context.UserSongs.AsNoTracking()
                .Where(us => us.UserId == user.UserId)
                .Select(us => us.LastPlayed)
                .ToArrayAsync());
    }

    [Fact]
    public async Task Handle_WhenSongDoesNotExist_ReturnsOkWithZeroUpdates()
    {
        // Arrange
        var user = await SeedUserAsync(DateTime.UtcNow);

        // Act
        var result = await _handler.Handle(
            new UpdateSongsLastPlayedCommand(user.UserId, new[] { new SongLastPlayed(Guid.NewGuid().ToString(), DateTime.UtcNow) }),
            CancellationToken.None);

        // Assert
        // An id with no song is not a client error -- a client still holding a song the server has since deleted is
        // the ordinary case. Failing here 400s the entire batch and strands the client's outbox. Nothing is created.
        Assert.True(result.IsOk);
        Assert.Equal(0, result.Get().UpdatedCount);
        Assert.False(await _database.Context.UserSongs.AnyAsync(us => us.UserId == user.UserId));
    }
}
