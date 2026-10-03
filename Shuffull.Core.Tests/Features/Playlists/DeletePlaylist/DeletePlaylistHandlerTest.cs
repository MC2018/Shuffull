using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Features.Playlists.DeletePlaylist;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Services;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Features.Playlists.DeletePlaylist;

/// <summary>
/// Covers deleting a playlist and the audition-purge that rides along with an exploratory one: only songs the
/// user never kept (still Exploratory) that no other playlist references are removed — rows + media — while
/// kept/promoted songs and songs on another playlist survive. Non-exploratory playlists purge nothing, and a
/// missing/not-owned playlist is an idempotent no-op.
/// </summary>
public class DeletePlaylistHandlerTest : IDisposable
{
    private readonly DatabaseFixture _database;
    private readonly RecordingMediaStore _mediaStore = new();
    private readonly DeletePlaylistHandler _handler;

    public DeletePlaylistHandlerTest()
    {
        _database = new DatabaseFixture();
        _handler = new DeletePlaylistHandler(_database.Context, _mediaStore);
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Records the media it was asked to delete; never touches disk.</summary>
    private sealed class RecordingMediaStore : ISongMediaStore
    {
        public List<(string FileHash, string FileExtension)> Deleted { get; } = [];
        public Task<Result> DeleteSongMediaAsync(string fileHash, string fileExtension, CancellationToken cancellationToken = default)
        {
            Deleted.Add((fileHash, fileExtension));
            return Task.FromResult(Result.Ok());
        }
    }

    private async Task<User> SeedUserAsync()
    {
        var user = new User
        {
            UserId = Guid.NewGuid().ToString(),
            Username = $"user-{Guid.NewGuid():N}",
            Version = DateTime.UtcNow.AddDays(-1),
            ServerHash = "server-hash",
        };
        await _database.Context.Users.AddAsync(user);
        await _database.Context.SaveChangesAsync();
        return user;
    }

    private async Task<Playlist> SeedPlaylistAsync(string userId, bool isExploratory)
    {
        var playlist = new Playlist
        {
            PlaylistId = Guid.NewGuid().ToString(),
            UserId = userId,
            Name = "Playlist",
            CurrentSongId = null,
            PercentUntilReplayable = 0.9m,
            IsExploratory = isExploratory,
            Version = DateTime.UtcNow.AddDays(-1),
        };
        await _database.Context.Playlists.AddAsync(playlist);
        await _database.Context.SaveChangesAsync();
        return playlist;
    }

    private async Task<Song> SeedSongAsync(bool exploratory)
    {
        var song = new Song
        {
            SongId = Guid.NewGuid().ToString(),
            Name = "Song",
            FileExtension = ".mp3",
            FileHash = Guid.NewGuid().ToString(),
            Exploratory = exploratory,
            Version = DateTime.UtcNow,
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

    /// <summary>Gives the song a full set of dependent rows so the purge's cascade can be asserted.</summary>
    private async Task SeedSongChildrenAsync(Song song, string userId)
    {
        var artist = new Artist { ArtistId = Guid.NewGuid().ToString(), Name = "Artist" };
        var mood = new Mood { TagId = Guid.NewGuid().ToString(), Name = $"Mood-{Guid.NewGuid():N}" };
        await _database.Context.Artists.AddAsync(artist);
        await _database.Context.AddAsync(mood);
        await _database.Context.SongArtists.AddAsync(new SongArtist { SongArtistId = Guid.NewGuid().ToString(), SongId = song.SongId, ArtistId = artist.ArtistId });
        await _database.Context.SongTags.AddAsync(new SongTag { SongTagId = Guid.NewGuid().ToString(), SongId = song.SongId, TagId = mood.TagId });
        await _database.Context.UserSongs.AddAsync(new UserSong { UserId = userId, SongId = song.SongId, LastPlayed = DateTime.MinValue, Version = DateTime.UtcNow, LikeStatus = LikeStatus.Neutral });
        await _database.Context.SongReplacements.AddAsync(new SongReplacement { SongReplacementId = Guid.NewGuid().ToString(), SongId = song.SongId, Status = SongReplacementStatus.Pending, CreatedAt = DateTime.UtcNow });
        await _database.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task Delete_ExploratoryPlaylist_PurgesUnkeptSong_KeepsPromoted()
    {
        var user = await SeedUserAsync();
        var playlist = await SeedPlaylistAsync(user.UserId, isExploratory: true);
        var kept = await SeedSongAsync(exploratory: false);       // promoted by an earlier keep
        var unkept = await SeedSongAsync(exploratory: true);      // never kept
        await AddJoinAsync(playlist.PlaylistId, kept.SongId);
        await AddJoinAsync(playlist.PlaylistId, unkept.SongId);
        await SeedSongChildrenAsync(unkept, user.UserId);

        var result = await _handler.Handle(new DeletePlaylistCommand(user.UserId, playlist.PlaylistId), CancellationToken.None);

        Assert.True(result.IsOk);
        Assert.True(result.Get().Deleted);
        Assert.Equal([unkept.SongId], result.Get().PurgedSongIds);

        // Playlist gone; unkept song and all its dependents gone; kept song survives.
        Assert.False(await _database.Context.Playlists.AsNoTracking().AnyAsync(p => p.PlaylistId == playlist.PlaylistId));
        Assert.False(await _database.Context.Songs.AsNoTracking().AnyAsync(s => s.SongId == unkept.SongId));
        Assert.True(await _database.Context.Songs.AsNoTracking().AnyAsync(s => s.SongId == kept.SongId));
        Assert.False(await _database.Context.SongArtists.AsNoTracking().AnyAsync(sa => sa.SongId == unkept.SongId));
        Assert.False(await _database.Context.SongTags.AsNoTracking().AnyAsync(st => st.SongId == unkept.SongId));
        Assert.False(await _database.Context.UserSongs.AsNoTracking().AnyAsync(us => us.SongId == unkept.SongId));
        Assert.False(await _database.Context.SongReplacements.AsNoTracking().AnyAsync(sr => sr.SongId == unkept.SongId));
        Assert.False(await _database.Context.PlaylistSongs.AsNoTracking().AnyAsync(ps => ps.SongId == unkept.SongId));

        // Media cleanup was requested for exactly the purged song.
        Assert.Equal([(unkept.FileHash, unkept.FileExtension)], _mediaStore.Deleted);
    }

    [Fact]
    public async Task Delete_ExploratoryPlaylist_KeepsSongStillOnAnotherPlaylist()
    {
        var user = await SeedUserAsync();
        var auditionList = await SeedPlaylistAsync(user.UserId, isExploratory: true);
        var otherList = await SeedPlaylistAsync(user.UserId, isExploratory: false);
        var song = await SeedSongAsync(exploratory: true);
        await AddJoinAsync(auditionList.PlaylistId, song.SongId);
        await AddJoinAsync(otherList.PlaylistId, song.SongId);

        var result = await _handler.Handle(new DeletePlaylistCommand(user.UserId, auditionList.PlaylistId), CancellationToken.None);

        Assert.True(result.Get().Deleted);
        Assert.Empty(result.Get().PurgedSongIds);
        Assert.True(await _database.Context.Songs.AsNoTracking().AnyAsync(s => s.SongId == song.SongId));
        Assert.False(await _database.Context.PlaylistSongs.AsNoTracking().AnyAsync(ps => ps.PlaylistId == auditionList.PlaylistId));
        Assert.True(await _database.Context.PlaylistSongs.AsNoTracking().AnyAsync(ps => ps.PlaylistId == otherList.PlaylistId && ps.SongId == song.SongId));
        Assert.Empty(_mediaStore.Deleted);
    }

    [Fact]
    public async Task Delete_NonExploratoryPlaylist_PurgesNothing()
    {
        var user = await SeedUserAsync();
        var playlist = await SeedPlaylistAsync(user.UserId, isExploratory: false);
        var song = await SeedSongAsync(exploratory: true); // an exploratory song that happens to live in a normal list
        await AddJoinAsync(playlist.PlaylistId, song.SongId);

        var result = await _handler.Handle(new DeletePlaylistCommand(user.UserId, playlist.PlaylistId), CancellationToken.None);

        Assert.True(result.Get().Deleted);
        Assert.Empty(result.Get().PurgedSongIds);
        Assert.False(await _database.Context.Playlists.AsNoTracking().AnyAsync(p => p.PlaylistId == playlist.PlaylistId));
        Assert.True(await _database.Context.Songs.AsNoTracking().AnyAsync(s => s.SongId == song.SongId)); // never purged from a normal list
        Assert.Empty(_mediaStore.Deleted);
    }

    [Fact]
    public async Task Delete_MissingPlaylist_IsIdempotentNoOp()
    {
        var user = await SeedUserAsync();

        var result = await _handler.Handle(new DeletePlaylistCommand(user.UserId, Guid.NewGuid().ToString()), CancellationToken.None);

        Assert.True(result.IsOk);
        Assert.False(result.Get().Deleted);
        Assert.Empty(result.Get().PurgedSongIds);
    }

    [Fact]
    public async Task Delete_PlaylistOwnedByAnotherUser_IsNoOpAndLeavesItIntact()
    {
        var owner = await SeedUserAsync();
        var intruder = await SeedUserAsync();
        var playlist = await SeedPlaylistAsync(owner.UserId, isExploratory: true);
        var song = await SeedSongAsync(exploratory: true);
        await AddJoinAsync(playlist.PlaylistId, song.SongId);

        var result = await _handler.Handle(new DeletePlaylistCommand(intruder.UserId, playlist.PlaylistId), CancellationToken.None);

        Assert.True(result.IsOk);
        Assert.False(result.Get().Deleted);
        Assert.True(await _database.Context.Playlists.AsNoTracking().AnyAsync(p => p.PlaylistId == playlist.PlaylistId));
        Assert.True(await _database.Context.Songs.AsNoTracking().AnyAsync(s => s.SongId == song.SongId));
        Assert.Empty(_mediaStore.Deleted);
    }
}
