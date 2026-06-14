using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Features.Playlists.AddSongToPlaylist;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Features.Playlists.AddSongToPlaylist;

public class AddSongToPlaylistHandlerTest : IDisposable
{
    private readonly DatabaseFixture _database;
    private readonly AddSongToPlaylistHandler _handler;

    public AddSongToPlaylistHandlerTest()
    {
        _database = new DatabaseFixture();
        _handler = new AddSongToPlaylistHandler(_database.UnitOfWork);
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

    [Fact]
    public async Task Handle_WithValidCommand_AddsSongAndBumpsVersion()
    {
        // Arrange
        var user = await SeedUserAsync();
        var playlist = await SeedPlaylistAsync(user.UserId);
        var song = await SeedSongAsync();
        var originalVersion = playlist.Version;

        // Act
        var result = await _handler.Handle(
            new AddSongToPlaylistCommand(user.UserId, playlist.PlaylistId, song.SongId),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        Assert.True(result.Get().Added);

        var joinExists = await _database.Context.PlaylistSongs.AsNoTracking()
            .AnyAsync(ps => ps.PlaylistId == playlist.PlaylistId && ps.SongId == song.SongId);
        Assert.True(joinExists);

        var savedPlaylist = await _database.Context.Playlists.AsNoTracking()
            .SingleAsync(p => p.PlaylistId == playlist.PlaylistId);
        Assert.True(savedPlaylist.Version > originalVersion);
    }

    [Fact]
    public async Task Handle_WhenSongAlreadyOnPlaylist_ReturnsAddedFalse()
    {
        // Arrange
        var user = await SeedUserAsync();
        var playlist = await SeedPlaylistAsync(user.UserId);
        var song = await SeedSongAsync();
        await _database.Context.PlaylistSongs.AddAsync(new PlaylistSong
        {
            PlaylistSongId = Guid.NewGuid().ToString(),
            PlaylistId = playlist.PlaylistId,
            SongId = song.SongId,
        });
        await _database.Context.SaveChangesAsync();

        // Act
        var result = await _handler.Handle(
            new AddSongToPlaylistCommand(user.UserId, playlist.PlaylistId, song.SongId),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsOk);
        Assert.False(result.Get().Added);

        var joinCount = await _database.Context.PlaylistSongs.AsNoTracking()
            .CountAsync(ps => ps.PlaylistId == playlist.PlaylistId && ps.SongId == song.SongId);
        Assert.Equal(1, joinCount);
    }

    [Fact]
    public async Task Handle_WithMissingPlaylist_ReturnsError()
    {
        // Arrange
        var user = await SeedUserAsync();
        var song = await SeedSongAsync();

        // Act
        var result = await _handler.Handle(
            new AddSongToPlaylistCommand(user.UserId, Guid.NewGuid().ToString(), song.SongId),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        Assert.Contains("Playlist not found", result.GetError().Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_WithMissingSong_ReturnsError()
    {
        // Arrange
        var user = await SeedUserAsync();
        var playlist = await SeedPlaylistAsync(user.UserId);

        // Act
        var result = await _handler.Handle(
            new AddSongToPlaylistCommand(user.UserId, playlist.PlaylistId, Guid.NewGuid().ToString()),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        Assert.Contains("Song not found", result.GetError().Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_WithPlaylistOwnedByDifferentUser_ReturnsError()
    {
        // Arrange — the playlist belongs to someone else, so the requesting user must not touch it.
        var owner = await SeedUserAsync();
        var otherUser = await SeedUserAsync();
        var playlist = await SeedPlaylistAsync(owner.UserId);
        var song = await SeedSongAsync();

        // Act
        var result = await _handler.Handle(
            new AddSongToPlaylistCommand(otherUser.UserId, playlist.PlaylistId, song.SongId),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        Assert.Contains("Playlist not found", result.GetError().Message, StringComparison.OrdinalIgnoreCase);

        var joinExists = await _database.Context.PlaylistSongs.AsNoTracking()
            .AnyAsync(ps => ps.PlaylistId == playlist.PlaylistId && ps.SongId == song.SongId);
        Assert.False(joinExists);
    }
}
