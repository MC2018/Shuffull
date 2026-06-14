namespace Shuffull.Core.Persistence.Specifications;

/// <summary>
/// Specification pattern interface for encapsulating query logic.
/// </summary>
/// <typeparam name="T">Entity type</typeparam>
public interface ISpecification<T> where T : class
{
    /// <summary>
    /// Apply the specification to a queryable.
    /// This is called by the Repository and orchestrates the query building.
    /// </summary>
    /// <param name="query">The base queryable</param>
    /// <returns>The transformed query</returns>
    IQueryable<T> Apply(IQueryable<T> query);
}
