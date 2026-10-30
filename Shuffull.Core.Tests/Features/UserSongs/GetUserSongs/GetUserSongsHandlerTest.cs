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

    [Fact]
    public async Task Handle_KeysetCursor_PagesThroughATieLargerThanAPage_EachRecordOnce()
    {
        // Arrange — 1,200 play records sharing one Version (shuffull-workspace#38), plus another user's record at the
        // same Version that must never appear. Inserted shuffled so the order has to come from the query.
        var user = await SeedUserAsync();
        var otherUser = await SeedUserAsync();
        var version = new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc).AddTicks(1234560);
        var songIds = Enumerable.Range(0, 1200).Select(i => $"song-{i:0000}").ToList();
        await _database.Context.Songs.AddRangeAsync(songIds.Select(id => new Song
        {
            SongId = id,
            Name = "Song",
            FileExtension = ".mp3",
            FileHash = $"hash-{id}",
            ExternalSongId = null,
        }));
        await _database.Context.UserSongs.AddRangeAsync(Enumerable.Range(0, songIds.Count)
            .Select(i => songIds[i * 7 % songIds.Count])
            .Select(id => new UserSong { UserId = user.UserId, SongId = id, LastPlayed = version, Version = version })
            .Append(new UserSong { UserId = otherUser.UserId, SongId = songIds[600], LastPlayed = version, Version = version }));
        await _database.Context.SaveChangesAsync();

        // Act — page from the last record's exact Version and SongId.
        var pages = new List<GetUserSongsResponse>();
        var query = new GetUserSongsQuery(user.UserId, DateTime.MinValue);
        while (pages.Count < 10)
        {
            var result = await _handler.Handle(query, CancellationToken.None);
            Assert.True(result.IsOk);
            var page = result.Get();
            pages.Add(page);
            if (page.EndOfList)
            {
                break;
            }

            query = new GetUserSongsQuery(user.UserId, page.UserSongs[^1].Version, page.UserSongs[^1].SongId);
        }

        // Assert
        Assert.Equal(new[] { 500, 500, 200 }, pages.Select(p => p.UserSongs.Count).ToArray());
        Assert.Equal(new[] { false, false, true }, pages.Select(p => p.EndOfList).ToArray());
        Assert.Equal(songIds, pages.SelectMany(p => p.UserSongs).Select(us => us.SongId).ToList());
        Assert.All(pages.SelectMany(p => p.UserSongs), us => Assert.Equal(user.UserId, us.UserId));
    }

    [Fact]
    public async Task Handle_WithoutAfterId_KeepsTheLegacyStrictlyAfterCursor()
    {
        // Arrange
        var user = await SeedUserAsync();
        var cutoff = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedUserSongAsync(user.UserId, cutoff);
        var newer = await SeedUserSongAsync(user.UserId, cutoff.AddTicks(10));

        // Act
        var result = await _handler.Handle(new GetUserSongsQuery(user.UserId, cutoff), CancellationToken.None);

        // Assert — the record at exactly the cursor stays excluded, as installed apps expect.
        Assert.Equal(new[] { newer.SongId }, result.Get().UserSongs.Select(us => us.SongId).ToArray());
    }
}
