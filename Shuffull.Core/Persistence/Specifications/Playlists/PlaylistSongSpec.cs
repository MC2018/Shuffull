using Shuffull.Core.Persistence.Specifications;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Persistence.Specifications.Playlists;

/// <summary>
/// Matches the join row linking a specific song to a specific playlist. Used to detect whether a
/// song is already on a playlist before adding it again.
/// </summary>
public class PlaylistSongSpec : BaseSpecification<PlaylistSong>
{
    private readonly string _playlistId;
    private readonly string _songId;

    public PlaylistSongSpec(string playlistId, string songId)
    {
        _playlistId = playlistId;
        _songId = songId;
    }

    protected override IQueryable<PlaylistSong> BuildQuery(IQueryable<PlaylistSong> query)
        => query.Where(playlistSong => playlistSong.PlaylistId == _playlistId && playlistSong.SongId == _songId);
}
