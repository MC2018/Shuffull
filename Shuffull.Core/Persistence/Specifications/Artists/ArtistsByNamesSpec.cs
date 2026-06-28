using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Persistence.Specifications.Artists;

/// <summary>
/// Loads existing artists whose name is in the supplied set, so callers can reuse a shared
/// <see cref="Artist"/> master row rather than creating a duplicate. Tracked so reused rows can be linked.
/// </summary>
public class ArtistsByNamesSpec : BaseSpecification<Artist>
{
    private readonly List<string> _names;

    public ArtistsByNamesSpec(IEnumerable<string> names)
    {
        _names = names.ToList();
    }

    protected override IQueryable<Artist> BuildQuery(IQueryable<Artist> query)
        => query.Where(artist => _names.Contains(artist.Name));
}
