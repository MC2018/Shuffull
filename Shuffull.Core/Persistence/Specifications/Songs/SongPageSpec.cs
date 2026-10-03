using Microsoft.EntityFrameworkCore;
using Shuffull.Core.Persistence.Specifications;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Persistence.Specifications.Songs;

/// <summary>
/// Loads a single fixed-size page of songs ordered by id, with artists and tags eagerly included so
/// each can be projected to a <see cref="Models.Dtos.SongDto"/> without lazy loading. Ordering by id
/// makes paging deterministic (the legacy SongController.GetAll paged without an order-by).
/// </summary>
public class SongPageSpec : BaseSpecification<Song>
{
    /// <summary>Page size, mirroring the legacy SongController's MAX_PAGE_LENGTH.</summary>
    public const int PageSize = 500;

    private readonly int _pageIndex;

    public SongPageSpec(int pageIndex)
    {
        _pageIndex = pageIndex;
    }

    protected override IQueryable<Song> BuildQuery(IQueryable<Song> query)
        => query
            .OrderBy(song => song.SongId)
            .Skip(_pageIndex * PageSize)
            .Take(PageSize)
            .Include(song => song.SongArtists)
                .ThenInclude(songArtist => songArtist.Artist)
            .Include(song => song.SongTags)
                .ThenInclude(songTag => songTag.Tag);
}
