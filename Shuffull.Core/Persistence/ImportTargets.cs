using Microsoft.EntityFrameworkCore;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;
using Shuffull.Shared.Tools;

namespace Shuffull.Core.Persistence;

/// <summary>
/// Links a song to the user and playlist an import (or a song request) was for. Shared by the importer and the song
/// request instant path, so a song linked straight away and a song downloaded first land in the same place the same way.
/// </summary>
public static class ImportTargets
{
    /// <summary>
    /// Stages an import's target for <paramref name="song"/>, without saving: resolves (or creates) the target
    /// playlist and adds the song to it, and gives the user a UserSong. Each link is added only if it is missing,
    /// so a song that already exists (a duplicate import) is linked without doubling anything up. A playlist or
    /// UserSong that gains the song gets a fresh Version, so clients sync it.
    /// </summary>
    /// <param name="importedForAudition">The import's audition intent; decides a newly created playlist's status.</param>
    public static async Task LinkAsync(ShuffullContext dbContext, Song song, string userId, string? playlistId, string? playlistName, bool importedForAudition, LikeStatus likeStatus, CancellationToken cancellationToken = default!)
    {
        // Resolve the target playlist: prefer an existing one by id; otherwise, if a name was provided,
        // reuse a same-named playlist for this user (idempotent across imports) or create one attached to them.
        Playlist? playlist = null;
        if (!string.IsNullOrEmpty(playlistId))
        {
            playlist = await dbContext.Playlists.FindAsync([playlistId], cancellationToken: cancellationToken);
        }

        if (playlist == null && !string.IsNullOrWhiteSpace(playlistName))
        {
            playlist = await dbContext.Playlists
                .FirstOrDefaultAsync(p => p.UserId == userId && p.Name == playlistName, cancellationToken);

            if (playlist == null)
            {
                playlist = new Playlist
                {
                    PlaylistId = string.IsNullOrWhiteSpace(playlistId) ? IdGenerator.Generate() : playlistId,
                    UserId = userId,
                    Name = playlistName,
                    CurrentSongId = null,
                    PercentUntilReplayable = 0.9m,
                    // The first song imported into a freshly-created playlist decides its audition status: an
                    // exploratory source imports its songs untagged into a dedicated target playlist, so that
                    // playlist becomes an audition playlist (and a later delete can purge the un-kept songs).
                    IsExploratory = importedForAudition,
                    Version = DateTime.UtcNow
                };
                dbContext.Playlists.Add(playlist);
            }
        }

        if (playlist != null)
        {
            var alreadyInPlaylist = await dbContext.PlaylistSongs
                .AnyAsync(ps => ps.PlaylistId == playlist.PlaylistId && ps.SongId == song.SongId, cancellationToken);
            if (!alreadyInPlaylist)
            {
                var playlistSong = new PlaylistSong
                {
                    PlaylistSongId = IdGenerator.Generate(),
                    PlaylistId = playlist.PlaylistId,
                    SongId = song.SongId
                };
                dbContext.PlaylistSongs.Add(playlistSong);
                // Bump the playlist's version so clients re-fetch its (now-changed) song list. Without this, a
                // playlist that gains songs after its first sync stays frozen on the client — it only re-pulls a
                // playlist whose server Version increased — so newly-added songs never appear in it.
                playlist.Version = DateTime.UtcNow;
            }
        }

        // A user who already has the song keeps their UserSong as it is: a re-import must not overwrite a
        // like/dislike or play history they set here.
        var hasUserSong = await dbContext.UserSongs
            .AnyAsync(us => us.UserId == userId && us.SongId == song.SongId, cancellationToken);
        if (!hasUserSong)
        {
            var userSong = new UserSong()
            {
                UserId = userId,
                SongId = song.SongId,
                LastPlayed = DateTime.MinValue,
                Version = DateTime.UtcNow,
                LikeStatus = likeStatus
            };
            dbContext.UserSongs.Add(userSong);

            // A like is a decision to keep the song (Shuffull#38): never leave a liked song Exploratory. This only
            // ever PROMOTES an existing song; an audition import never demotes a kept one.
            if (song.Exploratory && likeStatus.RetainsSong())
            {
                song.Exploratory = false;
                song.Version = DateTime.UtcNow;
            }
        }
    }
}
