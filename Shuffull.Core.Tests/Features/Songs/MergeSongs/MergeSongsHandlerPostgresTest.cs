using Microsoft.EntityFrameworkCore;
using Npgsql;
using Nut.Results;
using Shuffull.Core.Features.Songs.MergeSongs;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Features.Songs.MergeSongs;

/// <summary>
/// The merge against real Postgres, where a Like or a library add from another session can overlap it. Whichever
/// commits first is moved to the survivor; nothing is cascaded away with the loser. Skipped unless
/// SHUFFULL_TEST_POSTGRES is set.
/// </summary>
public class MergeSongsHandlerPostgresTest : IAsyncLifetime
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

    private async Task<User> SeedUserAsync(bool curator = false)
    {
        await using var context = Database.CreateContext();
        var user = new User
        {
            UserId = Guid.NewGuid().ToString(), Username = $"user-{Guid.NewGuid():N}", Version = DateTime.UtcNow, ServerHash = "server-hash",
            IsCurator = curator,
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    private async Task<Song> SeedSongAsync()
    {
        await using var context = Database.CreateContext();
        var song = new Song
        {
            SongId = Guid.NewGuid().ToString(), Name = "Song", FileExtension = ".mp3", FileHash = Guid.NewGuid().ToString(), Version = DateTime.UtcNow,
        };
        context.Songs.Add(song);
        await context.SaveChangesAsync();
        return song;
    }

    private async Task AddUserSongAsync(string userId, string songId, LikeStatus likeStatus)
    {
        await using var context = Database.CreateContext();
        context.UserSongs.Add(new UserSong { UserId = userId, SongId = songId, LastPlayed = DateTime.UtcNow, Version = DateTime.UtcNow, LikeStatus = likeStatus });
        await context.SaveChangesAsync();
    }

    private async Task<Result<MergeSongsResponse>> MergeAsync(string curatorId, string survivorId, string loserId)
    {
        await using var context = Database.CreateContext();
        return await new MergeSongsHandler(context).Handle(new MergeSongsCommand(curatorId, survivorId, loserId), CancellationToken.None);
    }

    private async Task<UserSong?> UserSongAsync(string userId, string songId)
    {
        await using var context = Database.CreateContext();
        return await context.UserSongs.AsNoTracking().SingleOrDefaultAsync(us => us.UserId == userId && us.SongId == songId);
    }

    [PostgresFact]
    public async Task Merge_MovesEverythingAndDeletesTheLoser_OnPostgres()
    {
        var curator = await SeedUserAsync(curator: true);
        var me = await SeedUserAsync();
        var survivor = await SeedSongAsync();
        var loser = await SeedSongAsync();
        await AddUserSongAsync(me.UserId, survivor.SongId, LikeStatus.Neutral);
        await AddUserSongAsync(me.UserId, loser.SongId, LikeStatus.Love);

        var result = await MergeAsync(curator.UserId, survivor.SongId, loser.SongId);

        Assert.True(result.IsOk, result.IsError ? result.GetError().Message : null);
        Assert.Equal(LikeStatus.Love, (await UserSongAsync(me.UserId, survivor.SongId))!.LikeStatus);
        await using var context = Database.CreateContext();
        Assert.False(await context.Songs.AnyAsync(s => s.SongId == loser.SongId));
        Assert.True(await context.SongTombstones.AnyAsync(t => t.SongId == loser.SongId));
        Assert.True(await context.SongMerges.AnyAsync(m => m.LoserSongId == loser.SongId && m.SurvivorSongId == survivor.SongId));
    }

    [PostgresFact]
    public async Task Merge_LikeInFlight_MergeWaitsAndMovesTheLike()
    {
        var curator = await SeedUserAsync(curator: true);
        var me = await SeedUserAsync();
        var survivor = await SeedSongAsync();
        var loser = await SeedSongAsync();
        await AddUserSongAsync(me.UserId, loser.SongId, LikeStatus.Neutral);

        // A Like on the loser that has written but not committed yet.
        await using var likeConnection = await Database.OpenConnectionAsync();
        await using var like = await likeConnection.BeginTransactionAsync();
        await using (var update = new NpgsqlCommand(
            "UPDATE \"UserSongs\" SET \"LikeStatus\" = @like WHERE \"UserId\" = @user AND \"SongId\" = @song", likeConnection, like))
        {
            update.Parameters.AddWithValue("like", (int)LikeStatus.Like);
            update.Parameters.AddWithValue("user", me.UserId);
            update.Parameters.AddWithValue("song", loser.SongId);
            await update.ExecuteNonQueryAsync();
        }

        var merge = MergeAsync(curator.UserId, survivor.SongId, loser.SongId);
        Assert.True(await Database.WaitForLockWaiterAsync());
        await like.CommitAsync();
        var result = await merge;

        Assert.True(result.IsOk, result.IsError ? result.GetError().Message : null);
        Assert.Equal(LikeStatus.Like, (await UserSongAsync(me.UserId, survivor.SongId))!.LikeStatus);
        Assert.Null(await UserSongAsync(me.UserId, loser.SongId));
    }

    [PostgresFact]
    public async Task Merge_LibraryAddInFlight_MergeWaitsAndMovesTheAdd()
    {
        var curator = await SeedUserAsync(curator: true);
        var other = await SeedUserAsync();
        var survivor = await SeedSongAsync();
        var loser = await SeedSongAsync();

        // Another user's library add of the loser, written but not committed. Its foreign-key check holds a share lock
        // on the loser's song row, which the merge's FOR UPDATE waits for.
        await using var addConnection = await Database.OpenConnectionAsync();
        await using var add = await addConnection.BeginTransactionAsync();
        await using (var insert = new NpgsqlCommand(
            "INSERT INTO \"UserSongs\" (\"UserId\", \"SongId\", \"LastPlayed\", \"Version\", \"LikeStatus\") VALUES (@user, @song, now(), now(), 0)",
            addConnection, add))
        {
            insert.Parameters.AddWithValue("user", other.UserId);
            insert.Parameters.AddWithValue("song", loser.SongId);
            await insert.ExecuteNonQueryAsync();
        }

        var merge = MergeAsync(curator.UserId, survivor.SongId, loser.SongId);
        Assert.True(await Database.WaitForLockWaiterAsync());
        await add.CommitAsync();
        var result = await merge;

        Assert.True(result.IsOk, result.IsError ? result.GetError().Message : null);
        Assert.NotNull(await UserSongAsync(other.UserId, survivor.SongId));
    }
}
