using Shuffull.Core.Persistence.Specifications;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Persistence.Specifications.Tags;

/// <summary>
/// Loads every tag (across the Genre/Language/TimePeriod TPH hierarchy) ordered by type then name so
/// the result is deterministic.
/// </summary>
public class AllTagsSpec : BaseSpecification<Tag>
{
    protected override IQueryable<Tag> BuildQuery(IQueryable<Tag> query)
        => query
            .OrderBy(tag => tag.Type)
            .ThenBy(tag => tag.Name);
}
