using Microsoft.EntityFrameworkCore;
using Shuffull.Core.Persistence.Specifications;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Persistence.Specifications.Playlists;

/// <summary>
/// Loads a specific set of the user's playlists (by id), with the song join rows included so each
/// <see cref="Models.Dtos.PlaylistDto"/> reports its song ids. Scoped to the owning user so callers
/// cannot read another user's playlists. Song details themselves are not loaded — only the join ids.
/// </summary>
public class PlaylistsByUserAndIdsSpec : BaseSpecification<Playlist>
{
    private readonly string _userId;
    private readonly IReadOnlyCollection<string> _playlistIds;

    public PlaylistsByUserAndIdsSpec(string userId, IReadOnlyCollection<string> playlistIds)
    {
        _userId = userId;
        _playlistIds = playlistIds;
    }

    protected override IQueryable<Playlist> BuildQuery(IQueryable<Playlist> query)
        => query
            .Where(playlist => playlist.UserId == _userId && _playlistIds.Contains(playlist.PlaylistId))
            .OrderBy(playlist => playlist.Name)
            .Include(playlist => playlist.PlaylistSongs);
}
