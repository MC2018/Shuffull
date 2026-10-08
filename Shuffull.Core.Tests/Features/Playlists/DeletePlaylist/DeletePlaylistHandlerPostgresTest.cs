using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Nut.Results;
using Shuffull.Core.Features.Playlists.DeletePlaylist;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Features.Playlists.DeletePlaylist;

/// <summary>
/// The audition purge against real Postgres, where two sessions really do overlap. Covers what in-memory SQLite can't:
/// the purge's row lock, and writes from other sessions that land before or during it. Whichever commits first wins:
/// a Keep or library add that got there first spares the song, and an insert that arrives mid-purge waits and then
/// fails on its foreign key. Nothing is silently deleted. Skipped unless SHUFFULL_TEST_POSTGRES is set.
/// </summary>
public class DeletePlaylistHandlerPostgresTest : IAsyncLifetime
{
    private PostgresDatabase? _database;

    private PostgresDatabase Database => _database ?? throw new InvalidOperationException("Postgres is not configured.");

    public async Task InitializeAsync()
    {
        if (PostgresFactAttribute.BaseConnectionString is { Length: > 0 } baseConnectionString)
        {
            _database = await PostgresDatabase.CreateAsync(baseConnectionString);
        }
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }

    /// <summary>
    /// Runs <paramref name="action"/> once, right after the purge's first claim UPDATE executes. At that point the
    /// purge holds its row locks and has not committed.
    /// </summary>
    private sealed class AfterFirstClaim(Func<Task> action) : DbCommandInterceptor
    {
        private bool _fired;

        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (!_fired && command.CommandText.TrimStart().StartsWith("UPDATE \"Songs\"", StringComparison.Ordinal))
            {
                _fired = true;
                await action();
            }

            return result;
        }
    }

    private async Task<User> SeedUserAsync()
    {
        await using var context = Database.CreateContext();
        var user = new User { UserId = Guid.NewGuid().ToString(), Username = $"user-{Guid.NewGuid():N}", Version = DateTime.UtcNow, ServerHash = "server-hash" };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    private async Task<Playlist> SeedAuditionPlaylistAsync(string userId)
    {
        await using var context = Database.CreateContext();
        var playlist = new Playlist { PlaylistId = Guid.NewGuid().ToString(), UserId = userId, Name = "Mix", PercentUntilReplayable = 0.5m, IsExploratory = true, Version = DateTime.UtcNow };
        context.Playlists.Add(playlist);
        await context.SaveChangesAsync();
        return playlist;
    }

    private async Task<Song> SeedSongOnPlaylistAsync(string playlistId)
    {
        await using var context = Database.CreateContext();
        var song = new Song { SongId = Guid.NewGuid().ToString(), Name = "Song", FileExtension = ".mp3", FileHash = Guid.NewGuid().ToString(), Exploratory = true, Version = DateTime.UtcNow };
        context.Songs.Add(song);
        context.PlaylistSongs.Add(new PlaylistSong { PlaylistSongId = Guid.NewGuid().ToString(), PlaylistId = playlistId, SongId = song.SongId });
        await context.SaveChangesAsync();
        return song;
    }

    private async Task AddUserSongAsync(string userId, string songId, LikeStatus likeStatus)
    {
        await using var context = Database.CreateContext();
        context.UserSongs.Add(new UserSong { UserId = userId, SongId = songId, LastPlayed = DateTime.UtcNow, Version = DateTime.UtcNow, LikeStatus = likeStatus });
        await context.SaveChangesAsync();
    }

    /// <summary>Another user adding the song to their library, as CreateUserSongs does, on its own connection.</summary>
    private static async Task InsertUserSongAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string userId, string songId)
    {
        await using var command = new NpgsqlCommand(
            "INSERT INTO \"UserSongs\" (\"UserId\", \"SongId\", \"LastPlayed\", \"Version\", \"LikeStatus\") VALUES (@user, @song, now(), now(), 0)",
            connection, transaction);
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.AddWithValue("song", songId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<Result<DeletePlaylistResponse>> PurgeAsync(string userId, string playlistId, params IInterceptor[] interceptors)
    {
        await using var context = Database.CreateContext(interceptors);
        return await new DeletePlaylistHandler(context).Handle(new DeletePlaylistCommand(userId, playlistId), CancellationToken.None);
    }

    private async Task<bool> SongExistsAsync(string songId)
    {
        await using var context = Database.CreateContext();
        return await context.Songs.AnyAsync(s => s.SongId == songId);
    }

    [PostgresFact]
    public async Task Delete_ExploratoryPlaylist_AppliesThePurgeRulesOnPostgres()
    {
        var user = await SeedUserAsync();
        var otherUser = await SeedUserAsync();
        var playlist = await SeedAuditionPlaylistAsync(user.UserId);
        var unkept = await SeedSongOnPlaylistAsync(playlist.PlaylistId);
        var loved = await SeedSongOnPlaylistAsync(playlist.PlaylistId);
        var shared = await SeedSongOnPlaylistAsync(playlist.PlaylistId);
        await AddUserSongAsync(user.UserId, unkept.SongId, LikeStatus.Dislike);
        await AddUserSongAsync(user.UserId, loved.SongId, LikeStatus.Love);
        await AddUserSongAsync(otherUser.UserId, shared.SongId, LikeStatus.Neutral);

        var result = await PurgeAsync(user.UserId, playlist.PlaylistId);

        Assert.True(result.IsOk, result.IsError ? result.GetError().Message : null);
        Assert.Equal([unkept.SongId], result.Get().PurgedSongIds);
        Assert.False(await SongExistsAsync(unkept.SongId));
        Assert.True(await SongExistsAsync(loved.SongId));
        Assert.True(await SongExistsAsync(shared.SongId));
        await using var context = Database.CreateContext();
        Assert.True(await context.SongTombstones.AnyAsync(t => t.SongId == unkept.SongId));
        Assert.False(await context.Playlists.AnyAsync(p => p.PlaylistId == playlist.PlaylistId));
    }

    [PostgresFact]
    public async Task Delete_ExploratoryPlaylist_PromotionInFlight_PurgeWaitsAndSparesTheSong()
    {
        var user = await SeedUserAsync();
        var playlist = await SeedAuditionPlaylistAsync(user.UserId);
        var song = await SeedSongOnPlaylistAsync(playlist.PlaylistId);

        // A Keep that has written but not committed yet.
        await using var keepConnection = await Database.OpenConnectionAsync();
        await using var keep = await keepConnection.BeginTransactionAsync();
        await using (var promote = new NpgsqlCommand("UPDATE \"Songs\" SET \"Exploratory\" = false WHERE \"SongId\" = @id", keepConnection, keep))
        {
            promote.Parameters.AddWithValue("id", song.SongId);
            await promote.ExecuteNonQueryAsync();
        }

        var purge = PurgeAsync(user.UserId, playlist.PlaylistId);
        Assert.True(await Database.WaitForLockWaiterAsync());
        await keep.CommitAsync();
        var result = await purge;

        Assert.True(result.IsOk, result.IsError ? result.GetError().Message : null);
        Assert.Empty(result.Get().PurgedSongIds);
        Assert.True(await SongExistsAsync(song.SongId));
    }

    [PostgresFact]
    public async Task Delete_ExploratoryPlaylist_LibraryAddInFlight_PurgeWaitsAndSparesTheSong()
    {
        var user = await SeedUserAsync();
        var otherUser = await SeedUserAsync();
        var playlist = await SeedAuditionPlaylistAsync(user.UserId);
        var song = await SeedSongOnPlaylistAsync(playlist.PlaylistId);

        // Another user's library add that has written but not committed yet.
        await using var addConnection = await Database.OpenConnectionAsync();
        await using var add = await addConnection.BeginTransactionAsync();
        await InsertUserSongAsync(addConnection, add, otherUser.UserId, song.SongId);

        var purge = PurgeAsync(user.UserId, playlist.PlaylistId);
        Assert.True(await Database.WaitForLockWaiterAsync());
        await add.CommitAsync();
        var result = await purge;

        Assert.True(result.IsOk, result.IsError ? result.GetError().Message : null);
        Assert.Empty(result.Get().PurgedSongIds);
        Assert.True(await SongExistsAsync(song.SongId));
        await using var context = Database.CreateContext();
        Assert.True(await context.UserSongs.AnyAsync(us => us.SongId == song.SongId && us.UserId == otherUser.UserId));
    }

    [PostgresFact]
    public async Task Delete_ExploratoryPlaylist_LibraryAddArrivingMidPurge_WaitsThenFailsInsteadOfBeingDeleted()
    {
        var user = await SeedUserAsync();
        var otherUser = await SeedUserAsync();
        var playlist = await SeedAuditionPlaylistAsync(user.UserId);
        var song = await SeedSongOnPlaylistAsync(playlist.PlaylistId);

        await using var addConnection = await Database.OpenConnectionAsync();
        Task? libraryAdd = null;
        var addWaitedForPurge = false;
        var afterClaim = new AfterFirstClaim(async () =>
        {
            libraryAdd = InsertUserSongAsync(addConnection, null, otherUser.UserId, song.SongId);
            addWaitedForPurge = await Database.WaitForLockWaiterAsync();
        });

        var result = await PurgeAsync(user.UserId, playlist.PlaylistId, afterClaim);

        Assert.True(result.IsOk, result.IsError ? result.GetError().Message : null);
        Assert.Equal([song.SongId], result.Get().PurgedSongIds);
        Assert.True(addWaitedForPurge, "The library add went through without waiting for the purge.");
        var failure = await Assert.ThrowsAsync<PostgresException>(() => libraryAdd!);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, failure.SqlState);
    }
}
