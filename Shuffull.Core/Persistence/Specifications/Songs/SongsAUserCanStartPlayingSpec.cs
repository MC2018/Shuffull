using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Persistence.Specifications.Songs;

/// <summary>
/// The songs among <c>songIds</c> that a play by <c>userId</c> may add to their library, with no artists or tags
/// loaded. That leaves out a song still <c>Exploratory</c> that is on none of the user's playlists: the state
/// <c>DeletePlaylistHandler</c> leaves when it drops the user's link to an audition song someone else still has
/// (Shuffull#87). A stale play from a device that hasn't synced the delete yet must not link it again. A first play
/// of an audition song is unaffected, since the song is still on the user's mix.
/// </summary>
public class SongsAUserCanStartPlayingSpec(string userId, IReadOnlyCollection<string> songIds) : BaseSpecification<Song>
{
    protected override IQueryable<Song> BuildQuery(IQueryable<Song> query)
        => query.Where(song => songIds.Contains(song.SongId)
            && (!song.Exploratory || song.PlaylistSongs.Any(ps => ps.Playlist.UserId == userId)));
}
