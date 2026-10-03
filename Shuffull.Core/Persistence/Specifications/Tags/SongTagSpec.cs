using Shuffull.Core.Persistence.Specifications;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Persistence.Specifications.Tags;

/// <summary>
/// Matches the join row linking a specific tag to a specific song. Used to detect whether a song
/// already carries a tag before adding it again.
/// </summary>
public class SongTagSpec : BaseSpecification<SongTag>
{
    private readonly string _songId;
    private readonly string _tagId;

    public SongTagSpec(string songId, string tagId)
    {
        _songId = songId;
        _tagId = tagId;
    }

    protected override IQueryable<SongTag> BuildQuery(IQueryable<SongTag> query)
        => query.Where(songTag => songTag.SongId == _songId && songTag.TagId == _tagId);
}
