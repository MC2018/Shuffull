using Microsoft.EntityFrameworkCore;
using Shuffull.Core.Persistence.Specifications;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Persistence.Specifications.Songs;

/// <summary>
/// Loads songs changed after a given version, ordered by version then id, for incremental sync — with artists
/// and tags eagerly included so each projects to a <see cref="Models.Dtos.SongDto"/> without lazy loading. Takes
/// one row beyond <see cref="PageSize"/> so the caller can tell whether more pages remain (mirrors
/// UserSongsByUserAfterDateSpec).
/// <para>
/// With <c>afterId</c> the cursor is the keyset (Version, SongId) of the last row the client has: rows that tie
/// on that Version page by id, so a tie of any size can't repeat a page or skip a row (Shuffull#64). Without it
/// the filter is <c>Version > afterDate</c> alone, which is what installed apps still send.
/// </para>
/// </summary>
public class SongsByVersionAfterDateSpec : BaseSpecification<Song>
{
    public const int PageSize = 500;

    private readonly DateTime _afterDate;
    private readonly string? _afterId;

    public SongsByVersionAfterDateSpec(DateTime afterDate, string? afterId = null)
    {
        _afterDate = afterDate;
        _afterId = string.IsNullOrEmpty(afterId) ? null : afterId;
    }

    protected override IQueryable<Song> BuildQuery(IQueryable<Song> query)
        => (_afterId == null
                ? query.Where(song => song.Version > _afterDate)
                : query.Where(song => song.Version > _afterDate
                    || (song.Version == _afterDate && string.Compare(song.SongId, _afterId) > 0)))
            .OrderBy(song => song.Version)
            .ThenBy(song => song.SongId)
            .Take(PageSize + 1)
            .Include(song => song.SongArtists)
                .ThenInclude(songArtist => songArtist.Artist)
            .Include(song => song.SongTags)
                .ThenInclude(songTag => songTag.Tag);
}
