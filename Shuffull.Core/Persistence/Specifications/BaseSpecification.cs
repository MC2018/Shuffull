using Microsoft.EntityFrameworkCore;

namespace Shuffull.Core.Persistence.Specifications;

/// <summary>
/// Base class for specifications with AsNoTracking support.
/// Override BuildQuery() to define your query logic.
/// Optionally call AsNoTracking() to enable read-only mode.
/// </summary>
/// <typeparam name="T">Entity type</typeparam>
public abstract class BaseSpecification<T> : ISpecification<T> where T : class
{
    private bool _useNoTracking = false;

    /// <summary>
    /// Enable AsNoTracking for read-only queries.
    /// </summary>
    public BaseSpecification<T> AsNoTracking()
    {
        _useNoTracking = true;
        return this;
    }

    public IQueryable<T> Apply(IQueryable<T> query)
    {
        var result = BuildQuery(query);
        return _useNoTracking ? result.AsNoTracking() : result;
    }

    /// <summary>
    /// Override this to define your query logic using standard LINQ.
    /// </summary>
    protected abstract IQueryable<T> BuildQuery(IQueryable<T> query);
}
