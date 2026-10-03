using Nut.Results;
using Shuffull.Core.Features.UserSongs.GetUserSongs;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Features.UserSongs.GetUserSongs;

public class GetUserSongsHandlerTest : IDisposable
{
    private readonly DatabaseFixture _database;
    private readonly GetUserSongsHandler _handler;

    public GetUserSongsHandlerTest()
    {
        _database = new DatabaseFixture();
        _handler = new GetUserSongsHandler(_database.UnitOfWork);
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

    private async Task<UserSong> SeedUserSongAsync(string userId, DateTime version)
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
            LastPlayed = DateTime.UtcNow.AddDays(-1),
            Version = version,
        };
        await _database.Context.UserSongs.AddAsync(userSong);
        await _database.Context.SaveChangesAsync();
        return userSong;
    }

    [Fact]
    public async Task Handle_ReturnsOnlyRecordsNewerThanAfterDate_OrderedByVersion()
    {
        // Arrange
        var user = await SeedUserAsync();
        var cutoff = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedUserSongAsync(user.UserId, cutoff.AddDays(-5)); // before cutoff — excluded
        var newer = await SeedUserSongAsync(user.UserId, cutoff.AddDays(5));
        var newest = await SeedUserSongAsync(user.UserId, cutoff.AddDays(10));

        // Act
        var result = await _handler.Handle(new GetUserSongsQuery(user.UserId, cutoff), CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        var response = result.Get();
        Assert.True(response.EndOfList);
        Assert.Equal(2, response.UserSongs.Count);
        Assert.Equal(newer.SongId, response.UserSongs[0].SongId);
        Assert.Equal(newest.SongId, response.UserSongs[1].SongId);
    }

    [Fact]
    public async Task Handle_OnlyReturnsRequestingUsersRecords()
    {
        // Arrange
        var user = await SeedUserAsync();
        var otherUser = await SeedUserAsync();
        await SeedUserSongAsync(otherUser.UserId, DateTime.UtcNow);

        // Act
        var result = await _handler.Handle(
            new GetUserSongsQuery(user.UserId, DateTime.MinValue), CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        Assert.Empty(result.Get().UserSongs);
        Assert.True(result.Get().EndOfList);
    }
}
