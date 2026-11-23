using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Persistence.Specifications.SongMerges;

/// <summary>The merges that took away any of these songs. The id set is expected to be bounded by the caller.</summary>
public class SongMergesByLoserIdsSpec : BaseSpecification<SongMerge>
{
    private readonly IReadOnlyCollection<string> _loserSongIds;

    public SongMergesByLoserIdsSpec(IReadOnlyCollection<string> loserSongIds)
    {
        _loserSongIds = loserSongIds;
    }

    protected override IQueryable<SongMerge> BuildQuery(IQueryable<SongMerge> query)
        => query.Where(merge => _loserSongIds.Contains(merge.LoserSongId));
}
