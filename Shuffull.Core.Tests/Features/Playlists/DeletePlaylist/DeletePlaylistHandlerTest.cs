using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Nut.Results;
using Shuffull.Core.Features.Playlists.DeletePlaylist;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Features.Playlists.DeletePlaylist;

/// <summary>
/// Covers deleting a playlist and the audition purge that comes with deleting an exploratory one. Only songs
/// nobody kept are removed: still Exploratory, on no other playlist, in no other user's library, and liked or loved
/// by nobody. Each purge leaves a tombstone, and media is never deleted inline (a sweep does that after a grace
/// window). Promoted songs, songs on another playlist, songs another user owns, liked songs, and a song promoted
/// while the purge runs all survive. Non-exploratory playlists purge nothing, and a missing/not-owned playlist is
/// an idempotent no-op.
/// </summary>
public class DeletePlaylistHandlerTest : IDisposable
{
    private readonly DatabaseFixture _database;
    private readonly DeletePlaylistHandler _handler;

    public DeletePlaylistHandlerTest()
    {
        _database = new DatabaseFixture();
        _handler = new DeletePlaylistHandler(_database.Context);
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Stands in for a concurrent Keep/Like. The first time the code under test is about to WRITE to <c>Songs</c>,
    /// it promotes <see cref="SongId"/> on the same connection and transaction. The promotion therefore lands after
    /// any earlier read of the song and before the purge's first write.
    /// </summary>
    private sealed class PromoteBeforeFirstSongWrite(string songId) : DbCommandInterceptor
    {
        public string SongId { get; } = songId;
        public bool Fired { get; private set; }

        private void PromoteOnce(DbCommand command)
        {
            var text = command.CommandText;
            if (Fired || !(text.Contains("UPDATE \"Songs\"") || text.Contains("DELETE FROM \"Songs\"")))
            {
                return;
            }

            Fired = true;
            using var promote = command.Connection!.CreateCommand();
            promote.Transaction = command.Transaction;
            promote.CommandText = "UPDATE \"Songs\" SET \"Exploratory\" = 0 WHERE \"SongId\" = $id";
            var id = promote.CreateParameter();
            id.ParameterName = "$id";
            id.Value = SongId;
            promote.Parameters.Add(id);
            promote.ExecuteNonQuery();
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            PromoteOnce(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            PromoteOnce(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
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

    private async Task AddUserSongAsync(string userId, string songId, LikeStatus likeStatus)
    {
        await _database.Context.UserSongs.AddAsync(new UserSong { UserId = userId, SongId = songId, LastPlayed = DateTime.MinValue, Version = DateTime.UtcNow, LikeStatus = likeStatus });
        await _database.Context.SaveChangesAsync();
    }

    private Task<bool> SongExistsAsync(string songId) =>
        _database.Context.Songs.AsNoTracking().AnyAsync(s => s.SongId == songId);

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
        await _database.Context.YoutubeRatingRequests.AddAsync(new YoutubeRatingRequest { YoutubeRatingRequestId = Guid.NewGuid().ToString(), SongId = song.SongId, VideoId = "video", Rating = YoutubeRating.Like, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        // The import that produced the song. It is history, not a dependent: the purge unlinks it rather than deleting it.
        await _database.Context.SongImports.AddAsync(new SongImport { SongImportId = Guid.NewGuid().ToString(), Name = "Import", ImportFolder = "imports", FileType = ".mp3", UserId = userId, SongId = song.SongId });
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
        Assert.False(await _database.Context.YoutubeRatingRequests.AsNoTracking().AnyAsync(r => r.SongId == unkept.SongId));
        Assert.False(await _database.Context.PlaylistSongs.AsNoTracking().AnyAsync(ps => ps.SongId == unkept.SongId));
        var import = Assert.Single(await _database.Context.SongImports.AsNoTracking().ToListAsync());
        Assert.Null(import.SongId);

        // A tombstone records the purge, holding the media for the sweep.
        var tombstone = Assert.Single(await _database.Context.SongTombstones.AsNoTracking().ToListAsync());
        Assert.Equal(unkept.SongId, tombstone.SongId);
        Assert.Equal(unkept.FileHash, tombstone.FileHash);
        Assert.Equal(unkept.FileExtension, tombstone.FileExtension);
        Assert.Equal(unkept.Name, tombstone.Name);
        Assert.Equal(user.UserId, tombstone.DeletedByUserId);
        Assert.Equal(playlist.PlaylistId, tombstone.PlaylistId);
        Assert.Null(tombstone.MediaSweptAt);
        Assert.False(tombstone.MediaDeleted);
    }

    [Theory]
    [InlineData(LikeStatus.Like)]
    [InlineData(LikeStatus.Love)]
    public async Task Delete_ExploratoryPlaylist_KeepsSongTheUserLikes(LikeStatus likeStatus)
    {
        // A song liked before a Like started promoting is still flagged Exploratory. The like itself must save it.
        var user = await SeedUserAsync();
        var playlist = await SeedPlaylistAsync(user.UserId, isExploratory: true);
        var liked = await SeedSongAsync(exploratory: true);
        await AddJoinAsync(playlist.PlaylistId, liked.SongId);
        await AddUserSongAsync(user.UserId, liked.SongId, likeStatus);

        var result = await _handler.Handle(new DeletePlaylistCommand(user.UserId, playlist.PlaylistId), CancellationToken.None);

        Assert.True(result.Get().Deleted);
        Assert.Empty(result.Get().PurgedSongIds);
        Assert.True(await SongExistsAsync(liked.SongId));
        Assert.True(await _database.Context.UserSongs.AsNoTracking().AnyAsync(us => us.SongId == liked.SongId && us.LikeStatus == likeStatus));
        Assert.Empty(await _database.Context.SongTombstones.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData(LikeStatus.Neutral)]
    [InlineData(LikeStatus.Dislike)]
    public async Task Delete_ExploratoryPlaylist_PurgesSongTheUserDidNotLike(LikeStatus likeStatus)
    {
        var user = await SeedUserAsync();
        var playlist = await SeedPlaylistAsync(user.UserId, isExploratory: true);
        var song = await SeedSongAsync(exploratory: true);
        await AddJoinAsync(playlist.PlaylistId, song.SongId);
        await AddUserSongAsync(user.UserId, song.SongId, likeStatus);

        var result = await _handler.Handle(new DeletePlaylistCommand(user.UserId, playlist.PlaylistId), CancellationToken.None);

        Assert.Equal([song.SongId], result.Get().PurgedSongIds);
        Assert.False(await SongExistsAsync(song.SongId));
    }

    [Fact]
    public async Task Delete_ExploratoryPlaylist_KeepsSongInAnotherUsersLibrary()
    {
        // Songs are shared rows. One user deleting their mix must not take the song, or the library entry, from
        // someone else.
        var user = await SeedUserAsync();
        var otherUser = await SeedUserAsync();
        var playlist = await SeedPlaylistAsync(user.UserId, isExploratory: true);
        var song = await SeedSongAsync(exploratory: true);
        await AddJoinAsync(playlist.PlaylistId, song.SongId);
        await AddUserSongAsync(user.UserId, song.SongId, LikeStatus.Neutral);
        await AddUserSongAsync(otherUser.UserId, song.SongId, LikeStatus.Neutral);

        var result = await _handler.Handle(new DeletePlaylistCommand(user.UserId, playlist.PlaylistId), CancellationToken.None);

        Assert.True(result.Get().Deleted);
        Assert.Empty(result.Get().PurgedSongIds);
        Assert.True(await SongExistsAsync(song.SongId));
        Assert.True(await _database.Context.UserSongs.AsNoTracking().AnyAsync(us => us.SongId == song.SongId && us.UserId == otherUser.UserId));
        Assert.Empty(await _database.Context.SongTombstones.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Delete_ExploratoryPlaylist_SongPromotedWhileThePurgeRuns_Survives()
    {
        var user = await SeedUserAsync();
        var playlist = await SeedPlaylistAsync(user.UserId, isExploratory: true);
        var racing = await SeedSongAsync(exploratory: true);
        var unkept = await SeedSongAsync(exploratory: true);
        await AddJoinAsync(playlist.PlaylistId, racing.SongId);
        await AddJoinAsync(playlist.PlaylistId, unkept.SongId);

        var promote = new PromoteBeforeFirstSongWrite(racing.SongId);
        using var racedContext = _database.CreateContext(promote);
        var handler = new DeletePlaylistHandler(racedContext);

        var result = await handler.Handle(new DeletePlaylistCommand(user.UserId, playlist.PlaylistId), CancellationToken.None);

        Assert.True(promote.Fired);
        Assert.True(result.IsOk);
        Assert.True(await SongExistsAsync(racing.SongId));
        Assert.DoesNotContain(racing.SongId, result.Get().PurgedSongIds);
        Assert.False(await _database.Context.SongTombstones.AsNoTracking().AnyAsync(t => t.SongId == racing.SongId));
        // The other, genuinely un-kept song is still purged: the race spares one song, not the whole purge.
        Assert.Equal([unkept.SongId], result.Get().PurgedSongIds);
        Assert.False(await SongExistsAsync(unkept.SongId));
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
        Assert.Empty(await _database.Context.SongTombstones.AsNoTracking().ToListAsync());
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
        Assert.Empty(await _database.Context.SongTombstones.AsNoTracking().ToListAsync());
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
        Assert.Empty(await _database.Context.SongTombstones.AsNoTracking().ToListAsync());
    }
}
