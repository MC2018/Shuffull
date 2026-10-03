using Nut.Results;
using Shuffull.Core.Features.Playlists.GetPlaylistDetails;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Features.Playlists.GetPlaylistDetails;

public class GetPlaylistDetailsHandlerTest : IDisposable
{
    private readonly DatabaseFixture _database;
    private readonly GetPlaylistDetailsHandler _handler;

    public GetPlaylistDetailsHandlerTest()
    {
        _database = new DatabaseFixture();
        _handler = new GetPlaylistDetailsHandler(_database.UnitOfWork);
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

    private async Task<Playlist> SeedPlaylistAsync(string userId)
    {
        var playlist = new Playlist
        {
            PlaylistId = Guid.NewGuid().ToString(),
            UserId = userId,
            Name = "Playlist",
            CurrentSongId = null,
            PercentUntilReplayable = 0.9m,
            Version = DateTime.UtcNow,
        };
        await _database.Context.Playlists.AddAsync(playlist);
        await _database.Context.SaveChangesAsync();
        return playlist;
    }

    private async Task<Song> SeedSongOnPlaylistAsync(string playlistId, string name)
    {
        var song = new Song
        {
            SongId = Guid.NewGuid().ToString(),
            Name = name,
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
    public async Task Handle_ReturnsPlaylistWithFullSongDetails()
    {
        // Arrange
        var user = await SeedUserAsync();
        var playlist = await SeedPlaylistAsync(user.UserId);
        var song1 = await SeedSongOnPlaylistAsync(playlist.PlaylistId, "First");
        var song2 = await SeedSongOnPlaylistAsync(playlist.PlaylistId, "Second");

        // Act
        var result = await _handler.Handle(
            new GetPlaylistDetailsQuery(user.UserId, playlist.PlaylistId), CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        var dto = result.Get().Playlist;
        Assert.Equal(playlist.PlaylistId, dto.PlaylistId);
        Assert.Equal(2, dto.Songs.Count);
        Assert.Contains(dto.Songs, s => s.SongId == song1.SongId && s.Name == "First");
        Assert.Contains(dto.Songs, s => s.SongId == song2.SongId && s.Name == "Second");
    }

    [Fact]
    public async Task Handle_WithMissingPlaylist_ReturnsError()
    {
        // Arrange
        var user = await SeedUserAsync();

        // Act
        var result = await _handler.Handle(
            new GetPlaylistDetailsQuery(user.UserId, Guid.NewGuid().ToString()), CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        Assert.Contains("Playlist not found", result.GetError().Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_WithPlaylistOwnedByAnotherUser_ReturnsError()
    {
        // Arrange — the playlist belongs to someone else; the caller must not be able to read it.
        var owner = await SeedUserAsync();
        var otherUser = await SeedUserAsync();
        var playlist = await SeedPlaylistAsync(owner.UserId);

        // Act
        var result = await _handler.Handle(
            new GetPlaylistDetailsQuery(otherUser.UserId, playlist.PlaylistId), CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        Assert.Contains("Playlist not found", result.GetError().Message, StringComparison.OrdinalIgnoreCase);
    }
}
