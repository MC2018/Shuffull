using Microsoft.EntityFrameworkCore;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Persistence.Specifications.Songs;

/// <summary>
/// Loads a single song by id with its artist and tag joins eagerly included, tracked (no AsNoTracking) so the
/// caller can rewrite those join collections in place. Used by the curator song-edit command.
/// </summary>
public class SongWithRelationsByIdSpec : BaseSpecification<Song>
{
    private readonly string _songId;

    public SongWithRelationsByIdSpec(string songId)
    {
        _songId = songId;
    }

    protected override IQueryable<Song> BuildQuery(IQueryable<Song> query)
        => query
            .Where(song => song.SongId == _songId)
            .Include(song => song.SongArtists)
                .ThenInclude(songArtist => songArtist.Artist)
            .Include(song => song.SongTags)
                .ThenInclude(songTag => songTag.Tag);
}
