using Shuffull.Core.Persistence.Specifications;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Persistence.Specifications.Playlists;

/// <summary>
/// Loads a single playlist by id, scoped to its owning user so callers cannot touch another user's
/// playlists. Intentionally does not call AsNoTracking when used for writes, so the loaded entity is
/// change-tracked and edits (e.g. bumping Version) are persisted on SaveChanges.
/// </summary>
public class PlaylistByUserAndIdSpec : BaseSpecification<Playlist>
{
    private readonly string _userId;
    private readonly string _playlistId;

    public PlaylistByUserAndIdSpec(string userId, string playlistId)
    {
        _userId = userId;
        _playlistId = playlistId;
    }

    protected override IQueryable<Playlist> BuildQuery(IQueryable<Playlist> query)
        => query.Where(playlist => playlist.UserId == _userId && playlist.PlaylistId == _playlistId);
}
