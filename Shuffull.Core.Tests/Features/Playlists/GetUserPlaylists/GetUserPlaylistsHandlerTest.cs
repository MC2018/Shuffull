using Nut.Results;
using Shuffull.Core.Features.Playlists.GetUserPlaylists;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Features.Playlists.GetUserPlaylists;

public class GetUserPlaylistsHandlerTest : IDisposable
{
    private readonly DatabaseFixture _database;
    private readonly GetUserPlaylistsHandler _handler;

    public GetUserPlaylistsHandlerTest()
    {
        _database = new DatabaseFixture();
        _handler = new GetUserPlaylistsHandler(_database.UnitOfWork);
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

    private async Task<Playlist> SeedPlaylistAsync(string userId, string name)
    {
        var playlist = new Playlist
        {
            PlaylistId = Guid.NewGuid().ToString(),
            UserId = userId,
            Name = name,
            CurrentSongId = null,
            PercentUntilReplayable = 0.9m,
            Version = DateTime.UtcNow,
        };
        await _database.Context.Playlists.AddAsync(playlist);
        await _database.Context.SaveChangesAsync();
        return playlist;
    }

    [Fact]
    public async Task Handle_ReturnsOnlyTheUsersPlaylistsOrderedByName()
    {
        // Arrange — two playlists for our user (inserted out of order) and one for someone else.
        var user = await SeedUserAsync();
        var otherUser = await SeedUserAsync();
        await SeedPlaylistAsync(user.UserId, "Beta");
        await SeedPlaylistAsync(user.UserId, "Alpha");
        await SeedPlaylistAsync(otherUser.UserId, "Gamma");

        // Act
        var result = await _handler.Handle(new GetUserPlaylistsQuery(user.UserId), CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        var names = result.Get().Playlists.Select(p => p.Name).ToArray();
        Assert.Equal(new[] { "Alpha", "Beta" }, names);
    }

    [Fact]
    public async Task Handle_PopulatesSongIds()
    {
        // Arrange — one playlist with one song on it.
        var user = await SeedUserAsync();
        var playlist = await SeedPlaylistAsync(user.UserId, "Playlist");
        var song = new Song
        {
            SongId = Guid.NewGuid().ToString(),
            Name = "Song",
            FileExtension = ".mp3",
            FileHash = Guid.NewGuid().ToString(),
            ExternalSongId = null,
        };
        await _database.Context.Songs.AddAsync(song);
        await _database.Context.PlaylistSongs.AddAsync(new PlaylistSong
        {
            PlaylistSongId = Guid.NewGuid().ToString(),
            PlaylistId = playlist.PlaylistId,
            SongId = song.SongId,
        });
        await _database.Context.SaveChangesAsync();

        // Act
        var result = await _handler.Handle(new GetUserPlaylistsQuery(user.UserId), CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        var dto = Assert.Single(result.Get().Playlists);
        Assert.Equal(new[] { song.SongId }, dto.SongIds);
    }

    [Fact]
    public async Task Handle_WithNoPlaylists_ReturnsEmptyList()
    {
        // Arrange
        var user = await SeedUserAsync();

        // Act
        var result = await _handler.Handle(new GetUserPlaylistsQuery(user.UserId), CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        Assert.Empty(result.Get().Playlists);
    }
}
