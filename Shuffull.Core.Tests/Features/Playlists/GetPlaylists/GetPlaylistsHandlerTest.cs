using Nut.Results;
using Shuffull.Core.Features.Playlists.GetPlaylists;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Features.Playlists.GetPlaylists;

public class GetPlaylistsHandlerTest : IDisposable
{
    private readonly DatabaseFixture _database;
    private readonly GetPlaylistsHandler _handler;

    public GetPlaylistsHandlerTest()
    {
        _database = new DatabaseFixture();
        _handler = new GetPlaylistsHandler(_database.UnitOfWork);
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

    private async Task<Song> SeedSongOnPlaylistAsync(string playlistId)
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

        await _database.Context.PlaylistSongs.AddAsync(new PlaylistSong
        {
            PlaylistSongId = Guid.NewGuid().ToString(),
            PlaylistId = playlistId,
            SongId = song.SongId,
        });
        await _database.Context.SaveChangesAsync();
        return song;
    }

    [Fact]
    public async Task Handle_ReturnsRequestedPlaylistsWithSongIds()
    {
        // Arrange
        var user = await SeedUserAsync();
        var playlistA = await SeedPlaylistAsync(user.UserId, "Alpha");
        var playlistB = await SeedPlaylistAsync(user.UserId, "Beta");
        var songA = await SeedSongOnPlaylistAsync(playlistA.PlaylistId);

        // Act
        var result = await _handler.Handle(
            new GetPlaylistsQuery(user.UserId, new[] { playlistA.PlaylistId, playlistB.PlaylistId }),
            CancellationToken.None);

        // Assert — ordered by name (Alpha, Beta).
        Assert.True(result.IsOk);
        var playlists = result.Get().Playlists;
        Assert.Equal(2, playlists.Count);
        Assert.Equal(playlistA.PlaylistId, playlists[0].PlaylistId);
        Assert.Equal(new[] { songA.SongId }, playlists[0].SongIds);
        Assert.Equal(playlistB.PlaylistId, playlists[1].PlaylistId);
        Assert.Empty(playlists[1].SongIds);
    }

    [Fact]
    public async Task Handle_DoesNotReturnAnotherUsersPlaylists()
    {
        // Arrange
        var user = await SeedUserAsync();
        var otherUser = await SeedUserAsync();
        var ownPlaylist = await SeedPlaylistAsync(user.UserId, "Mine");
        var otherPlaylist = await SeedPlaylistAsync(otherUser.UserId, "Theirs");

        // Act — ask for both ids, but only the caller's own should come back.
        var result = await _handler.Handle(
            new GetPlaylistsQuery(user.UserId, new[] { ownPlaylist.PlaylistId, otherPlaylist.PlaylistId }),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        Assert.Single(result.Get().Playlists);
        Assert.Equal(ownPlaylist.PlaylistId, result.Get().Playlists[0].PlaylistId);
    }
}
