using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Features.Playlists.RemoveSongFromPlaylist;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Features.Playlists.RemoveSongFromPlaylist;

public class RemoveSongFromPlaylistHandlerTest : IDisposable
{
    private readonly DatabaseFixture _database;
    private readonly RemoveSongFromPlaylistHandler _handler;

    public RemoveSongFromPlaylistHandlerTest()
    {
        _database = new DatabaseFixture();
        _handler = new RemoveSongFromPlaylistHandler(_database.UnitOfWork);
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
            Version = DateTime.UtcNow.AddDays(-1),
        };
        await _database.Context.Playlists.AddAsync(playlist);
        await _database.Context.SaveChangesAsync();
        return playlist;
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

    private async Task AddJoinAsync(string playlistId, string songId)
    {
        await _database.Context.PlaylistSongs.AddAsync(new PlaylistSong
        {
            PlaylistSongId = Guid.NewGuid().ToString(),
            PlaylistId = playlistId,
            SongId = songId,
        });
        await _database.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task Handle_WhenSongOnPlaylist_RemovesJoinAndBumpsVersion()
    {
        // Arrange
        var user = await SeedUserAsync();
        var playlist = await SeedPlaylistAsync(user.UserId);
        var song = await SeedSongAsync();
        await AddJoinAsync(playlist.PlaylistId, song.SongId);
        var originalVersion = playlist.Version;

        // Act
        var result = await _handler.Handle(
            new RemoveSongFromPlaylistCommand(user.UserId, playlist.PlaylistId, song.SongId),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        Assert.True(result.Get().Removed);

        var joinExists = await _database.Context.PlaylistSongs.AsNoTracking()
            .AnyAsync(ps => ps.PlaylistId == playlist.PlaylistId && ps.SongId == song.SongId);
        Assert.False(joinExists);

        var savedPlaylist = await _database.Context.Playlists.AsNoTracking()
            .SingleAsync(p => p.PlaylistId == playlist.PlaylistId);
        Assert.True(savedPlaylist.Version > originalVersion);
    }

    [Fact]
    public async Task Handle_WhenSongNotOnPlaylist_ReturnsRemovedFalseAndDoesNotBumpVersion()
    {
        // Arrange — playlist exists but the song was never added; the operation is an idempotent no-op.
        var user = await SeedUserAsync();
        var playlist = await SeedPlaylistAsync(user.UserId);
        var song = await SeedSongAsync();
        var originalVersion = playlist.Version;

        // Act
        var result = await _handler.Handle(
            new RemoveSongFromPlaylistCommand(user.UserId, playlist.PlaylistId, song.SongId),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        Assert.False(result.Get().Removed);

        var savedPlaylist = await _database.Context.Playlists.AsNoTracking()
            .SingleAsync(p => p.PlaylistId == playlist.PlaylistId);
        Assert.Equal(originalVersion, savedPlaylist.Version);
    }

    [Fact]
    public async Task Handle_WithMissingPlaylist_ReturnsError()
    {
        // Arrange
        var user = await SeedUserAsync();
        var song = await SeedSongAsync();

        // Act
        var result = await _handler.Handle(
            new RemoveSongFromPlaylistCommand(user.UserId, Guid.NewGuid().ToString(), song.SongId),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        Assert.Contains("Playlist not found", result.GetError().Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_WithPlaylistOwnedByDifferentUser_ReturnsErrorAndKeepsJoin()
    {
        // Arrange — another user must not be able to remove songs from a playlist they don't own.
        var owner = await SeedUserAsync();
        var otherUser = await SeedUserAsync();
        var playlist = await SeedPlaylistAsync(owner.UserId);
        var song = await SeedSongAsync();
        await AddJoinAsync(playlist.PlaylistId, song.SongId);

        // Act
        var result = await _handler.Handle(
            new RemoveSongFromPlaylistCommand(otherUser.UserId, playlist.PlaylistId, song.SongId),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        Assert.Contains("Playlist not found", result.GetError().Message, StringComparison.OrdinalIgnoreCase);

        var joinExists = await _database.Context.PlaylistSongs.AsNoTracking()
            .AnyAsync(ps => ps.PlaylistId == playlist.PlaylistId && ps.SongId == song.SongId);
        Assert.True(joinExists);
    }
}
