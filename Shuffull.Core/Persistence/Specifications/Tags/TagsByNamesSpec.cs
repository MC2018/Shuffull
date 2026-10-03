using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Persistence.Specifications.Tags;

/// <summary>
/// Loads existing tags whose name is in the supplied set (across the TPH hierarchy). Callers narrow further by
/// <see cref="Tag.Type"/> in memory and reuse a matching master row instead of creating a duplicate. Tracked.
/// </summary>
public class TagsByNamesSpec : BaseSpecification<Tag>
{
    private readonly List<string> _names;

    public TagsByNamesSpec(IEnumerable<string> names)
    {
        _names = names.ToList();
    }

    protected override IQueryable<Tag> BuildQuery(IQueryable<Tag> query)
        => query.Where(tag => _names.Contains(tag.Name));
}
