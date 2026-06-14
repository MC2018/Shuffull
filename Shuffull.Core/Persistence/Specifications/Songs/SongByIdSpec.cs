using Microsoft.EntityFrameworkCore;
using Shuffull.Core.Persistence.Specifications;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Persistence.Specifications.Songs;

/// <summary>
/// Loads a single song by id with its artists and tags eagerly included, so the
/// <see cref="Models.Dtos.SongDto"/> can be projected without lazy loading.
/// </summary>
public class SongByIdSpec : BaseSpecification<Song>
{
    private readonly string _songId;

    public SongByIdSpec(string songId)
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
