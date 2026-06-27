using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Features.UserSongs.SetSongLikeStatus;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Features.UserSongs.SetSongLikeStatus;

public class SetSongLikeStatusHandlerTest : IDisposable
{
    private readonly DatabaseFixture _database;
    private readonly SetSongLikeStatusHandler _handler;

    public SetSongLikeStatusHandlerTest()
    {
        _database = new DatabaseFixture();
        _handler = new SetSongLikeStatusHandler(_database.UnitOfWork);
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

    private async Task<UserSong> SeedUserSongAsync(string userId, LikeStatus likeStatus, DateTime version)
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
            LastPlayed = DateTime.MinValue,
            Version = version,
            LikeStatus = likeStatus,
        };
        await _database.Context.UserSongs.AddAsync(userSong);
        await _database.Context.SaveChangesAsync();
        return userSong;
    }

    [Fact]
    public async Task Handle_WithNewStatus_UpdatesStatusAndBumpsVersions()
    {
        // Arrange
        var userVersion = DateTime.UtcNow.AddDays(-10);
        var user = await SeedUserAsync(userVersion);
        var songVersion = DateTime.UtcNow.AddDays(-5);
        var userSong = await SeedUserSongAsync(user.UserId, LikeStatus.Neutral, songVersion);

        // Act
        var result = await _handler.Handle(
            new SetSongLikeStatusCommand(user.UserId, userSong.SongId, LikeStatus.Love),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        Assert.Equal(LikeStatus.Love, result.Get().LikeStatus);
        Assert.Equal(userSong.SongId, result.Get().SongId);

        var savedUserSong = await _database.Context.UserSongs.AsNoTracking()
            .SingleAsync(us => us.UserId == user.UserId && us.SongId == userSong.SongId);
        Assert.Equal(LikeStatus.Love, savedUserSong.LikeStatus);
        Assert.True(savedUserSong.Version > songVersion);

        var savedUser = await _database.Context.Users.AsNoTracking().SingleAsync(u => u.UserId == user.UserId);
        Assert.True(savedUser.Version > userVersion);
    }

    [Fact]
    public async Task Handle_WithUnchangedStatus_IsNoOpAndLeavesVersionsUntouched()
    {
        // Arrange — setting the status to its current value should not bump any version.
        var userVersion = DateTime.UtcNow.AddDays(-10);
        var user = await SeedUserAsync(userVersion);
        var songVersion = DateTime.UtcNow.AddDays(-5);
        var userSong = await SeedUserSongAsync(user.UserId, LikeStatus.Like, songVersion);

        // Act
        var result = await _handler.Handle(
            new SetSongLikeStatusCommand(user.UserId, userSong.SongId, LikeStatus.Like),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        Assert.Equal(LikeStatus.Like, result.Get().LikeStatus);

        var savedUserSong = await _database.Context.UserSongs.AsNoTracking()
            .SingleAsync(us => us.UserId == user.UserId && us.SongId == userSong.SongId);
        Assert.Equal(songVersion, savedUserSong.Version);

        var savedUser = await _database.Context.Users.AsNoTracking().SingleAsync(u => u.UserId == user.UserId);
        Assert.Equal(userVersion, savedUser.Version);
    }

    [Fact]
    public async Task Handle_WhenNoMatchingUserSong_ReturnsError()
    {
        // Arrange
        var user = await SeedUserAsync(DateTime.UtcNow);

        // Act
        var result = await _handler.Handle(
            new SetSongLikeStatusCommand(user.UserId, Guid.NewGuid().ToString(), LikeStatus.Like),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        Assert.Contains("No matching song", result.GetError().Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_WithSongBelongingToAnotherUser_ReturnsError()
    {
        // Arrange — the song record belongs to a different user, so the requester has no matching UserSong.
        var owner = await SeedUserAsync(DateTime.UtcNow);
        var otherUser = await SeedUserAsync(DateTime.UtcNow);
        var userSong = await SeedUserSongAsync(owner.UserId, LikeStatus.Neutral, DateTime.UtcNow);

        // Act
        var result = await _handler.Handle(
            new SetSongLikeStatusCommand(otherUser.UserId, userSong.SongId, LikeStatus.Dislike),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        Assert.Contains("No matching song", result.GetError().Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(LikeStatus.Like)]
    [InlineData(LikeStatus.Love)]
    [InlineData(LikeStatus.Dislike)]
    [InlineData(LikeStatus.Neutral)]
    public async Task Handle_PersistsEachLikeStatusValue(LikeStatus target)
    {
        // Arrange — start from a status different from the target so the update path runs.
        var start = target == LikeStatus.Neutral ? LikeStatus.Like : LikeStatus.Neutral;
        var user = await SeedUserAsync(DateTime.UtcNow.AddDays(-1));
        var userSong = await SeedUserSongAsync(user.UserId, start, DateTime.UtcNow.AddDays(-1));

        // Act
        var result = await _handler.Handle(
            new SetSongLikeStatusCommand(user.UserId, userSong.SongId, target),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        var savedUserSong = await _database.Context.UserSongs.AsNoTracking()
            .SingleAsync(us => us.UserId == user.UserId && us.SongId == userSong.SongId);
        Assert.Equal(target, savedUserSong.LikeStatus);
    }
}
