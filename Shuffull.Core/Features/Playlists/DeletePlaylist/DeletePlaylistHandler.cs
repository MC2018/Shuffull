using MediatR;
using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Persistence;
using Shuffull.Core.Services;

namespace Shuffull.Core.Features.Playlists.DeletePlaylist;

/// <summary>
/// Deletes one of the user's playlists. For an exploratory ("audition") playlist it also purges the songs the
/// user never kept: any song this playlist held that is still <c>Exploratory</c> (never promoted by a keep) and
/// that no other playlist references is removed entirely — its joins/sentiment/open re-source request, the row,
/// and its stored media. A kept song was promoted (its <c>Exploratory</c> flag cleared by the re-tag) and
/// survives, as does any song still on another playlist. The DB deletes are one atomic SaveChanges; the media
/// cleanup runs best-effort afterwards.
/// </summary>
public class DeletePlaylistHandler(ShuffullContext context, ISongMediaStore mediaStore)
    : IRequestHandler<DeletePlaylistCommand, Result<DeletePlaylistResponse>>
{
    public async Task<Result<DeletePlaylistResponse>> Handle(DeletePlaylistCommand request, CancellationToken cancellationToken)
    {
        // Ownership-scoped load (a user can only delete their own playlist). Include the joins so we know its
        // songs and can remove them alongside the playlist.
        var playlist = await context.Playlists
            .Include(p => p.PlaylistSongs)
            .FirstOrDefaultAsync(p => p.PlaylistId == request.PlaylistId && p.UserId == request.UserId, cancellationToken);

        if (playlist is null)
        {
            // Idempotent: already gone (or not this user's). A no-op success keeps the app's offline delete
            // queue from wedging on a retry.
            return Result.Ok(new DeletePlaylistResponse(request.PlaylistId, Deleted: false, PurgedSongIds: []));
        }

        var songIds = playlist.PlaylistSongs.Select(ps => ps.SongId).Distinct().ToList();

        // Decide the purge set BEFORE mutating anything, so the "on another playlist?" check sees the true
        // current state. Only for an audition playlist, and only songs the user never kept (still Exploratory)
        // that no other playlist references.
        var purgedSongIds = new List<string>();
        var purgedMedia = new List<(string FileHash, string FileExtension)>();
        if (playlist.IsExploratory && songIds.Count > 0)
        {
            var stillExploratory = await context.Songs
                .Where(s => songIds.Contains(s.SongId) && s.Exploratory)
                .ToListAsync(cancellationToken);

            foreach (var song in stillExploratory)
            {
                var onAnotherPlaylist = await context.PlaylistSongs
                    .AnyAsync(ps => ps.SongId == song.SongId && ps.PlaylistId != playlist.PlaylistId, cancellationToken);
                if (onAnotherPlaylist)
                {
                    continue;
                }

                // Remove every trace of the un-kept audition song. Load-then-remove keeps it provider-agnostic
                // (no reliance on DB cascade, which the SQLite test schema doesn't configure like Postgres).
                context.SongArtists.RemoveRange(await context.SongArtists.Where(sa => sa.SongId == song.SongId).ToListAsync(cancellationToken));
                context.SongTags.RemoveRange(await context.SongTags.Where(st => st.SongId == song.SongId).ToListAsync(cancellationToken));
                context.UserSongs.RemoveRange(await context.UserSongs.Where(us => us.SongId == song.SongId).ToListAsync(cancellationToken));
                context.SongReplacements.RemoveRange(await context.SongReplacements.Where(sr => sr.SongId == song.SongId).ToListAsync(cancellationToken));
                context.PlaylistSongs.RemoveRange(await context.PlaylistSongs.Where(ps => ps.SongId == song.SongId).ToListAsync(cancellationToken));
                context.Songs.Remove(song);

                purgedSongIds.Add(song.SongId);
                purgedMedia.Add((song.FileHash, song.FileExtension));
            }
        }

        // Remove the playlist and any remaining song joins (for kept/surviving songs). The purged songs' joins
        // are already marked for deletion above; re-removing the same tracked rows is a harmless no-op.
        context.PlaylistSongs.RemoveRange(playlist.PlaylistSongs);
        context.Playlists.Remove(playlist);

        // Bump the user's version so clients pick up the removal on their next sync.
        var user = await context.Users.FirstOrDefaultAsync(u => u.UserId == request.UserId, cancellationToken);
        if (user is not null)
        {
            user.Version = DateTime.UtcNow;
        }

        try
        {
            // One SaveChanges = one implicit transaction: every delete commits together or not at all.
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            return Result.Error<DeletePlaylistResponse>(ex.Message);
        }

        // Best-effort media cleanup AFTER the rows are gone: an orphaned file is harmless, but deleting a file
        // for a row that survived a rollback would not be. Never fails the delete.
        foreach (var (fileHash, fileExtension) in purgedMedia)
        {
            await mediaStore.DeleteSongMediaAsync(fileHash, fileExtension, cancellationToken);
        }

        return Result.Ok(new DeletePlaylistResponse(playlist.PlaylistId, Deleted: true, purgedSongIds));
    }
}
