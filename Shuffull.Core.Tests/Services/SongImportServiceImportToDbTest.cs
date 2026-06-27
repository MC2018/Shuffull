using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Api.Services;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Services;

/// <summary>
/// Exercises SongImportService.ImportToDbCoreAsync (the transactional db-write half of a new-song import)
/// against the in-memory SQLite fixture. Focuses on the playlist-resolution branches and the
/// recently-added "bump Playlist.Version when a song is added" behavior.
/// </summary>
public class SongImportServiceImportToDbTest : IDisposable
{
    private readonly DatabaseFixture _database;

    public SongImportServiceImportToDbTest()
    {
        _database = new DatabaseFixture();
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private static Song NewSong(string id = "song-1")
        => new()
        {
            SongId = id,
            Name = "Imported",
            FileExtension = ".mp3",
            FileHash = $"hash-{id}",
            ExternalSongId = null,
            Version = DateTime.UtcNow,
        };

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

    private Task<Result> ImportAsync(
        Song song,
        string userId,
        List<Artist>? existingArtists = null,
        List<Artist>? newArtists = null,
        List<Tag>? existingTags = null,
        List<Tag>? newTags = null,
        string? playlistId = null,
        string? playlistName = null,
        LikeStatus likeStatus = LikeStatus.Neutral)
        => SongImportService.ImportToDbCoreAsync(
            _database.Context,
            song,
            existingArtists ?? new List<Artist>(),
            newArtists ?? new List<Artist>(),
            existingTags ?? new List<Tag>(),
            newTags ?? new List<Tag>(),
            userId,
            playlistId,
            playlistName,
            likeStatus,
            CancellationToken.None);

    [Fact]
    public async Task ImportToDbCoreAsync_PersistsSongArtistsTagsAndUserSong()
    {
        // Arrange
        var user = await SeedUserAsync();
        var song = NewSong();
        var newArtist = new Artist { ArtistId = "artist-1", Name = "New Artist" };
        var newTag = new Genre { TagId = "tag-1", Name = "Rock" };

        // Act
        var result = await ImportAsync(song, user.UserId,
            newArtists: new List<Artist> { newArtist },
            newTags: new List<Tag> { newTag },
            likeStatus: LikeStatus.Like);

        // Assert
        Assert.True(result.IsOk);

        Assert.True(await _database.Context.Songs.AsNoTracking().AnyAsync(s => s.SongId == song.SongId));
        Assert.True(await _database.Context.Artists.AsNoTracking().AnyAsync(a => a.ArtistId == "artist-1"));
        Assert.True(await _database.Context.Tags.AsNoTracking().AnyAsync(t => t.TagId == "tag-1"));
        Assert.True(await _database.Context.SongArtists.AsNoTracking()
            .AnyAsync(sa => sa.SongId == song.SongId && sa.ArtistId == "artist-1"));
        Assert.True(await _database.Context.SongTags.AsNoTracking()
            .AnyAsync(st => st.SongId == song.SongId && st.TagId == "tag-1"));

        var userSong = await _database.Context.UserSongs.AsNoTracking()
            .SingleAsync(us => us.UserId == user.UserId && us.SongId == song.SongId);
        Assert.Equal(LikeStatus.Like, userSong.LikeStatus);
        Assert.Equal(DateTime.MinValue, userSong.LastPlayed);
    }

    [Fact]
    public async Task ImportToDbCoreAsync_WithExistingArtistsAndTags_LinksWithoutDuplicatingMasterRows()
    {
        // Arrange — pre-existing master rows are passed as "existing" and must only be joined, not re-added.
        var user = await SeedUserAsync();
        var existingArtist = new Artist { ArtistId = "existing-artist", Name = "Known" };
        var existingTag = new Genre { TagId = "existing-tag", Name = "Jazz" };
        await _database.Context.Artists.AddAsync(existingArtist);
        await _database.Context.Tags.AddAsync(existingTag);
        await _database.Context.SaveChangesAsync();

        var song = NewSong();

        // Act
        var result = await ImportAsync(song, user.UserId,
            existingArtists: new List<Artist> { existingArtist },
            existingTags: new List<Tag> { existingTag });

        // Assert
        Assert.True(result.IsOk);
        Assert.Equal(1, await _database.Context.Artists.AsNoTracking().CountAsync(a => a.ArtistId == "existing-artist"));
        Assert.Equal(1, await _database.Context.Tags.AsNoTracking().CountAsync(t => t.TagId == "existing-tag"));
        Assert.True(await _database.Context.SongArtists.AsNoTracking()
            .AnyAsync(sa => sa.SongId == song.SongId && sa.ArtistId == "existing-artist"));
        Assert.True(await _database.Context.SongTags.AsNoTracking()
            .AnyAsync(st => st.SongId == song.SongId && st.TagId == "existing-tag"));
    }

    [Fact]
    public async Task ImportToDbCoreAsync_WithoutPlaylist_AddsNoPlaylistSong()
    {
        // Arrange
        var user = await SeedUserAsync();
        var song = NewSong();

        // Act
        var result = await ImportAsync(song, user.UserId);

        // Assert
        Assert.True(result.IsOk);
        Assert.False(await _database.Context.PlaylistSongs.AsNoTracking().AnyAsync(ps => ps.SongId == song.SongId));
    }

    [Fact]
    public async Task ImportToDbCoreAsync_WithExistingPlaylistId_AddsSongAndBumpsVersion()
    {
        // Arrange
        var user = await SeedUserAsync();
        var oldVersion = DateTime.UtcNow.AddDays(-3);
        var playlist = new Playlist
        {
            PlaylistId = "playlist-1",
            UserId = user.UserId,
            Name = "My List",
            PercentUntilReplayable = 0.9m,
            Version = oldVersion,
        };
        await _database.Context.Playlists.AddAsync(playlist);
        await _database.Context.SaveChangesAsync();

        var song = NewSong();

        // Act
        var result = await ImportAsync(song, user.UserId, playlistId: playlist.PlaylistId);

        // Assert
        Assert.True(result.IsOk);
        Assert.True(await _database.Context.PlaylistSongs.AsNoTracking()
            .AnyAsync(ps => ps.PlaylistId == playlist.PlaylistId && ps.SongId == song.SongId));

        var saved = await _database.Context.Playlists.AsNoTracking().SingleAsync(p => p.PlaylistId == playlist.PlaylistId);
        Assert.True(saved.Version > oldVersion);
    }

    [Fact]
    public async Task ImportToDbCoreAsync_WithPlaylistName_ReusesExistingSameNamedPlaylistForUser()
    {
        // Arrange — a same-named playlist for this user already exists; import should reuse it, not create a new one.
        var user = await SeedUserAsync();
        var oldVersion = DateTime.UtcNow.AddDays(-3);
        var existing = new Playlist
        {
            PlaylistId = "existing-named",
            UserId = user.UserId,
            Name = "Imports",
            PercentUntilReplayable = 0.9m,
            Version = oldVersion,
        };
        await _database.Context.Playlists.AddAsync(existing);
        await _database.Context.SaveChangesAsync();

        var song = NewSong();

        // Act
        var result = await ImportAsync(song, user.UserId, playlistName: "Imports");

        // Assert
        Assert.True(result.IsOk);
        Assert.Equal(1, await _database.Context.Playlists.AsNoTracking().CountAsync(p => p.UserId == user.UserId && p.Name == "Imports"));
        Assert.True(await _database.Context.PlaylistSongs.AsNoTracking()
            .AnyAsync(ps => ps.PlaylistId == existing.PlaylistId && ps.SongId == song.SongId));

        var saved = await _database.Context.Playlists.AsNoTracking().SingleAsync(p => p.PlaylistId == existing.PlaylistId);
        Assert.True(saved.Version > oldVersion);
    }

    [Fact]
    public async Task ImportToDbCoreAsync_WithPlaylistName_CreatesPlaylistWhenNoneExists()
    {
        // Arrange — no playlist by that name yet; one is created and attached to the user.
        var user = await SeedUserAsync();
        var song = NewSong();

        // Act
        var result = await ImportAsync(song, user.UserId, playlistName: "Fresh Playlist");

        // Assert
        Assert.True(result.IsOk);
        var created = await _database.Context.Playlists.AsNoTracking()
            .SingleAsync(p => p.UserId == user.UserId && p.Name == "Fresh Playlist");
        Assert.True(await _database.Context.PlaylistSongs.AsNoTracking()
            .AnyAsync(ps => ps.PlaylistId == created.PlaylistId && ps.SongId == song.SongId));
    }

    [Fact]
    public async Task ImportToDbCoreAsync_PlaylistNameScopedPerUser_DoesNotReuseAnotherUsersPlaylist()
    {
        // Arrange — another user has a same-named playlist; the importing user must get their own, not that one.
        var owner = await SeedUserAsync();
        var importer = await SeedUserAsync();
        var othersPlaylist = new Playlist
        {
            PlaylistId = "others",
            UserId = owner.UserId,
            Name = "Shared Name",
            PercentUntilReplayable = 0.9m,
            Version = DateTime.UtcNow.AddDays(-1),
        };
        await _database.Context.Playlists.AddAsync(othersPlaylist);
        await _database.Context.SaveChangesAsync();

        var song = NewSong();

        // Act
        var result = await ImportAsync(song, importer.UserId, playlistName: "Shared Name");

        // Assert
        Assert.True(result.IsOk);
        var importerPlaylist = await _database.Context.Playlists.AsNoTracking()
            .SingleAsync(p => p.UserId == importer.UserId && p.Name == "Shared Name");
        Assert.NotEqual(othersPlaylist.PlaylistId, importerPlaylist.PlaylistId);
        Assert.False(await _database.Context.PlaylistSongs.AsNoTracking()
            .AnyAsync(ps => ps.PlaylistId == othersPlaylist.PlaylistId && ps.SongId == song.SongId));
    }
}
