using Microsoft.EntityFrameworkCore;
using Shuffull.Core.Persistence.Specifications;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Persistence.Specifications.Songs;

/// <summary>
/// Loads every song whose id is in the supplied set, with artists and tags eagerly included so each
/// can be projected to a <see cref="Models.Dtos.SongDto"/> without lazy loading. The id set is
/// expected to be bounded by the caller (see GetSongListValidator).
/// </summary>
public class SongsByIdsSpec : BaseSpecification<Song>
{
    private readonly IReadOnlyCollection<string> _songIds;

    public SongsByIdsSpec(IReadOnlyCollection<string> songIds)
    {
        _songIds = songIds;
    }

    protected override IQueryable<Song> BuildQuery(IQueryable<Song> query)
        => query
            .Where(song => _songIds.Contains(song.SongId))
            .Include(song => song.SongArtists)
                .ThenInclude(songArtist => songArtist.Artist)
            .Include(song => song.SongTags)
                .ThenInclude(songTag => songTag.Tag);
}
