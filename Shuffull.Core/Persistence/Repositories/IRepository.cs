using Nut.Results;
using Shuffull.Core.Persistence.Specifications;

namespace Shuffull.Core.Persistence.Repositories;

/// <summary>
/// Generic repository interface for data access operations
/// </summary>
/// <typeparam name="T">Entity type</typeparam>
public interface IRepository<T> where T : class
{
    /// <summary>
    /// Get entity by ID
    /// </summary>
    Task<Result<T>> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get single entity matching specification
    /// </summary>
    Task<Result<T>> GetAsync(ISpecification<T> spec, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all entities matching specification
    /// </summary>
    Task<Result<List<T>>> ListAsync(ISpecification<T> spec, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all entities
    /// </summary>
    Task<Result<List<T>>> ListAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Count entities matching specification
    /// </summary>
    Task<Result<int>> CountAsync(ISpecification<T> spec, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if any entity matches specification
    /// </summary>
    Task<Result<bool>> AnyAsync(ISpecification<T> spec, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add new entity
    /// </summary>
    Task<Result<T>> AddAsync(T entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update existing entity
    /// </summary>
    Result Update(T entity);

    /// <summary>
    /// Delete entity
    /// </summary>
    Result Delete(T entity);

    /// <summary>
    /// Delete multiple entities
    /// </summary>
    Result DeleteRange(IEnumerable<T> entities);
}
