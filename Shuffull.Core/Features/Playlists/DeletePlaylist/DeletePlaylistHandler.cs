using MediatR;
using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Persistence;

namespace Shuffull.Core.Features.Playlists.DeletePlaylist;

/// <summary>
/// Deletes one of the user's playlists. For an exploratory ("audition") playlist it also purges the songs nobody
/// kept: a song this playlist held is removed entirely (its joins, sentiment, open re-source request and row) only
/// when ALL of these hold:
/// <list type="bullet">
/// <item>it is still <c>Exploratory</c> (no Keep or Like promoted it);</item>
/// <item>no other playlist references it;</item>
/// <item>no other user has it in their library (a <c>UserSong</c>), since songs are shared rows;</item>
/// <item>nobody likes or loves it. This catches songs liked before a Like started promoting them.</item>
/// </list>
/// Each purge leaves a <see cref="SongTombstone"/>. Media is NOT deleted here: the tombstone holds it for a grace
/// window and a sweep removes it later, and only when no live song shares the file hash.
/// <para>
/// A song that survives only because someone else still has it loses the deleting user's own link instead
/// (Shuffull#87): their <c>UserSong</c> is dropped when the song is still <c>Exploratory</c>, they don't like or love
/// it, and it is on none of their other playlists. Otherwise two users auditioning one song would each spare it from
/// the other's delete, and it would stay in both libraries for good. An <c>Exploratory</c> song is one nobody has kept
/// (a Keep, a Like and a non-audition import all clear it), so this never drops a song the user kept. The app's own
/// local delete already removes such a song (<c>deletePlaylistAndPurge</c>), so the server now matches it.
/// </para>
/// <para>
/// A song someone has already kept (no longer <c>Exploratory</c>) keeps every user's link. It can be in this mix
/// because a later audition import linked it, and then the user's link is clutter. But nothing yet records which user
/// kept a song, so dropping that link could drop a Keep. That case stays open on Shuffull#87.
/// </para>
/// <para>
/// The decision and the delete are one transaction. The candidate song rows are locked first, then each song is
/// claimed by a conditional UPDATE that re-checks every rule against committed state. Anything that commits
/// before the lock (a Keep, a Like, another user adding the song to their library or a playlist) makes the song
/// fail the check, so it survives. Anything that arrives after the lock waits for the purge to commit, then fails:
/// a promotion finds no exploratory row, and an insert referencing the song fails its foreign key. The old version
/// read the songs first and deleted them in a later SaveChanges, so a promotion that landed between those two
/// steps was purged anyway.
/// </para>
/// </summary>
public class DeletePlaylistHandler(ShuffullContext context)
    : IRequestHandler<DeletePlaylistCommand, Result<DeletePlaylistResponse>>
{
    public async Task<Result<DeletePlaylistResponse>> Handle(DeletePlaylistCommand request, CancellationToken cancellationToken)
    {
        // Ownership-scoped (a user can only delete their own playlist).
        var playlist = await context.Playlists
            .AsNoTracking()
            .Where(p => p.PlaylistId == request.PlaylistId && p.UserId == request.UserId)
            .Select(p => new { p.PlaylistId, p.IsExploratory })
            .FirstOrDefaultAsync(cancellationToken);

        if (playlist is null)
        {
            // Idempotent: already gone (or not this user's). A no-op success keeps the app's offline delete
            // queue from wedging on a retry.
            return Result.Ok(new DeletePlaylistResponse(request.PlaylistId, Deleted: false, PurgedSongIds: []));
        }

        var playlistId = playlist.PlaylistId;
        var userId = request.UserId;
        var now = DateTime.UtcNow;
        var purgedSongIds = new List<string>();

        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            if (playlist.IsExploratory)
            {
                var songIds = await context.PlaylistSongs
                    .Where(ps => ps.PlaylistId == playlistId)
                    .Select(ps => ps.SongId)
                    .Distinct()
                    .ToListAsync(cancellationToken);

                await LockSongRowsAsync(songIds, cancellationToken);

                foreach (var songId in songIds)
                {
                    // Claim the song. Every rule from the summary goes in the WHERE clause, so it is checked against
                    // the row as it is now, not against an earlier read.
                    var claimed = await context.Songs
                        .Where(s => s.SongId == songId
                            && s.Exploratory
                            && !context.PlaylistSongs.Any(ps => ps.SongId == s.SongId && ps.PlaylistId != playlistId)
                            && !context.UserSongs.Any(us => us.SongId == s.SongId
                                && (us.UserId != userId || us.LikeStatus == LikeStatus.Like || us.LikeStatus == LikeStatus.Love)))
                        .ExecuteUpdateAsync(set => set.SetProperty(s => s.Version, now), cancellationToken);

                    if (claimed == 1)
                    {
                        purgedSongIds.Add(songId);
                    }
                }

                // A song nobody kept that someone else still has: drop this user's own link to it. One conditional
                // DELETE per song, so like the claim it is checked against the row as it is now. The song row is
                // still locked, so it can't be promoted under us, and a Like on this UserSong that committed first
                // keeps it. Nothing is sent back for these: the app's local delete already removed them.
                foreach (var songId in songIds.Except(purgedSongIds))
                {
                    await context.UserSongs
                        .Where(us => us.UserId == userId
                            && us.SongId == songId
                            && us.LikeStatus != LikeStatus.Like
                            && us.LikeStatus != LikeStatus.Love
                            && context.Songs.Any(s => s.SongId == songId && s.Exploratory)
                            && !context.PlaylistSongs.Any(ps => ps.SongId == songId
                                && ps.PlaylistId != playlistId
                                && ps.Playlist.UserId == userId))
                        .ExecuteDeleteAsync(cancellationToken);
                }
            }

            if (purgedSongIds.Count > 0)
            {
                var purged = await context.Songs
                    .AsNoTracking()
                    .Where(s => purgedSongIds.Contains(s.SongId))
                    .Select(s => new { s.SongId, s.Name, s.ExternalSongId, s.FileHash, s.FileExtension })
                    .ToListAsync(cancellationToken);

                // The claimed rows stay locked until commit, so these deletes act only on songs that passed the
                // check. Dependents are removed explicitly rather than by relying on DB cascades, which keeps
                // this provider-agnostic.
                await context.SongArtists.Where(sa => purgedSongIds.Contains(sa.SongId)).ExecuteDeleteAsync(cancellationToken);
                await context.SongTags.Where(st => purgedSongIds.Contains(st.SongId)).ExecuteDeleteAsync(cancellationToken);
                await context.UserSongs.Where(us => purgedSongIds.Contains(us.SongId)).ExecuteDeleteAsync(cancellationToken);
                await context.SongReplacements.Where(sr => purgedSongIds.Contains(sr.SongId)).ExecuteDeleteAsync(cancellationToken);
                await context.YoutubeRatingRequests.Where(r => purgedSongIds.Contains(r.SongId)).ExecuteDeleteAsync(cancellationToken);
                // SongImport.SongId is an optional FK with no cascade, and every completed import sets it (Shuffull#56).
                // Clear it, so a purged song's import history can't fail the whole delete.
                await context.SongImports
                    .Where(si => si.SongId != null && purgedSongIds.Contains(si.SongId))
                    .ExecuteUpdateAsync(set => set.SetProperty(si => si.SongId, (string?)null), cancellationToken);
                await context.PlaylistSongs.Where(ps => purgedSongIds.Contains(ps.SongId)).ExecuteDeleteAsync(cancellationToken);
                await context.Songs.Where(s => purgedSongIds.Contains(s.SongId)).ExecuteDeleteAsync(cancellationToken);

                context.SongTombstones.AddRange(purged.Select(s => new SongTombstone
                {
                    SongId = s.SongId,
                    Name = s.Name,
                    ExternalSongId = s.ExternalSongId,
                    FileHash = s.FileHash,
                    FileExtension = s.FileExtension,
                    DeletedByUserId = userId,
                    PlaylistId = playlistId,
                    DeletedAt = now,
                }));
            }

            // Remove the playlist and the joins of the songs that survive it.
            await context.PlaylistSongs.Where(ps => ps.PlaylistId == playlistId).ExecuteDeleteAsync(cancellationToken);
            await context.Playlists.Where(p => p.PlaylistId == playlistId).ExecuteDeleteAsync(cancellationToken);

            // Bump the user's version so clients pick up the removal on their next sync.
            var user = await context.Users.FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken);
            if (user is not null)
            {
                user.Version = now;
            }

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Disposing the transaction rolls back every statement above, so nothing has been deleted. A database
            // fault is a 5xx, so the app's outbox retries the delete; a constraint the purge trips stays a 4xx, since
            // a retry would trip it again and a 5xx would stop the app's sync on it (Shuffull#41).
            context.ChangeTracker.Clear();
            return Result.Error<DeletePlaylistResponse>(ResultErrors.FromException(ex, ex.Message));
        }

        return Result.Ok(new DeletePlaylistResponse(playlistId, Deleted: true, purgedSongIds));
    }

    private const string NpgsqlProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";

    /// <summary>
    /// Takes a FOR UPDATE lock on the candidate songs. The claim's UPDATE alone isn't enough on Postgres: it only
    /// changes a non-key column, and the lock that takes doesn't conflict with the one an insert takes on the row
    /// its foreign key references. So a <c>UserSong</c> or <c>PlaylistSong</c> added during the purge would neither
    /// wait nor fail, and the song delete would then cascade it away without an error. FOR UPDATE does conflict, so
    /// such an insert either commits before this lock (and the claim sees it) or waits and then fails on its foreign
    /// key. Rows are locked in id order so two purges sharing songs can't deadlock. SQLite takes a database-wide
    /// write lock and has no FOR UPDATE, so it is skipped there.
    /// </summary>
    private async Task LockSongRowsAsync(List<string> songIds, CancellationToken cancellationToken)
    {
        if (songIds.Count == 0 || context.Database.ProviderName != NpgsqlProviderName)
        {
            return;
        }

        var ids = songIds.ToArray();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"Songs\" WHERE \"SongId\" = ANY({ids}) ORDER BY \"SongId\" FOR UPDATE",
            cancellationToken);
    }
}
