using MediatR;
using Microsoft.EntityFrameworkCore;
using Nut.Results;
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
                // SongImport.SongId is an optional FK with no cascade. Imports don't set it today; clear it anyway
                // so one stray link can't fail the whole delete.
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
            // Disposing the transaction rolls back every statement above, so nothing has been deleted.
            context.ChangeTracker.Clear();
            return Result.Error<DeletePlaylistResponse>(ex.Message);
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
