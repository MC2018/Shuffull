using Microsoft.EntityFrameworkCore;
using Moq;
using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Features.Playlists.RemoveSongFromPlaylist;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence.Repositories;
using Shuffull.Core.Persistence.Specifications;
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
        Assert.Equal(ErrorKind.NotFound, ResultErrors.KindOf(result.GetError()));
    }

    [Fact]
    public async Task Handle_WhenTheSongLookupFails_ReturnsTheFault_NotANoOp()
    {
        // A database fault while looking up the join row used to be read as "not on the playlist" and answered
        // with a successful no-op, so the app dropped its queued removal. It must come back as the fault (a 5xx).
        var playlist = new Playlist { PlaylistId = "p", UserId = "u", Name = "Playlist", Version = DateTime.UtcNow };
        var playlists = new Mock<IRepository<Playlist>>();
        playlists.Setup(r => r.GetAsync(It.IsAny<ISpecification<Playlist>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(playlist));
        var fault = new KindedError(ErrorKind.ServerFault, "Error retrieving entity: connection lost");
        var joins = new Mock<IRepository<PlaylistSong>>();
        joins.Setup(r => r.GetAsync(It.IsAny<ISpecification<PlaylistSong>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Error<PlaylistSong>(fault));
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.Repository<Playlist>()).Returns(playlists.Object);
        unitOfWork.Setup(u => u.Repository<PlaylistSong>()).Returns(joins.Object);

        var result = await new RemoveSongFromPlaylistHandler(unitOfWork.Object)
            .Handle(new RemoveSongFromPlaylistCommand("u", "p", "s"), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Same(fault, result.GetError());
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
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
