using Microsoft.EntityFrameworkCore;
using Shuffull.Core.Persistence.Specifications;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Persistence.Specifications.Playlists;

/// <summary>
/// Loads every playlist owned by a user, ordered by name, with the song join rows included so each
/// <see cref="Models.Dtos.PlaylistDto"/> reports its song ids. Song details themselves are not
/// loaded — only the ids on the join rows.
/// </summary>
public class PlaylistsByUserSpec : BaseSpecification<Playlist>
{
    private readonly string _userId;

    public PlaylistsByUserSpec(string userId)
    {
        _userId = userId;
    }

    protected override IQueryable<Playlist> BuildQuery(IQueryable<Playlist> query)
        => query
            .Where(playlist => playlist.UserId == _userId)
            .OrderBy(playlist => playlist.Name)
            .Include(playlist => playlist.PlaylistSongs);
}
