using Microsoft.EntityFrameworkCore;

namespace Shuffull.Core.Persistence;

/// <summary>
/// Row locks for the handlers that delete songs (the audition purge and the merge). Postgres only: SQLite takes a
/// database-wide write lock and has no FOR UPDATE, so each lock here does nothing there.
/// </summary>
public static class SongRowLocks
{
    private const string NpgsqlProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";

    public static bool IsPostgres(ShuffullContext context) => context.Database.ProviderName == NpgsqlProviderName;

    /// <summary>
    /// Takes a FOR UPDATE lock on the songs. An UPDATE of a non-key column alone isn't enough on Postgres: the lock it
    /// takes doesn't conflict with the one an insert takes on the row its foreign key references. So a
    /// <c>UserSong</c> or <c>PlaylistSong</c> added meanwhile would neither wait nor fail, and the song delete would then
    /// cascade it away without an error. FOR UPDATE does conflict, so such an insert either commits before this lock
    /// (and the caller sees it) or waits and then fails on its foreign key. Rows are locked in id order, in every
    /// caller, so two of them sharing songs can't deadlock.
    /// </summary>
    public static async Task LockSongsAsync(ShuffullContext context, IReadOnlyCollection<string> songIds, CancellationToken cancellationToken)
    {
        if (songIds.Count == 0 || !IsPostgres(context))
        {
            return;
        }

        var ids = songIds.ToArray();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"Songs\" WHERE \"SongId\" = ANY({ids}) ORDER BY \"SongId\" FOR UPDATE",
            cancellationToken);
    }
}
