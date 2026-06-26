using Microsoft.EntityFrameworkCore;
using Shuffull.Core.Persistence.Specifications;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Persistence.Specifications.Songs;

/// <summary>
/// Loads songs changed after a given version, ordered by version, for incremental sync — with artists and
/// tags eagerly included so each projects to a <see cref="Models.Dtos.SongDto"/> without lazy loading. Takes
/// one row beyond <see cref="PageSize"/> so the caller can tell whether more pages remain (mirrors
/// UserSongsByUserAfterDateSpec).
/// </summary>
public class SongsByVersionAfterDateSpec : BaseSpecification<Song>
{
    public const int PageSize = 500;

    private readonly DateTime _afterDate;

    public SongsByVersionAfterDateSpec(DateTime afterDate)
    {
        _afterDate = afterDate;
    }

    protected override IQueryable<Song> BuildQuery(IQueryable<Song> query)
        => query
            .Where(song => song.Version > _afterDate)
            .OrderBy(song => song.Version)
            .Take(PageSize + 1)
            .Include(song => song.SongArtists)
                .ThenInclude(songArtist => songArtist.Artist)
            .Include(song => song.SongTags)
                .ThenInclude(songTag => songTag.Tag);
}
