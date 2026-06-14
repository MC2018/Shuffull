using Nut.Results;

namespace Shuffull.Core.Persistence.Repositories;

/// <summary>
/// Unit of Work pattern interface for coordinating multiple repositories and transactions
/// </summary>
public interface IUnitOfWork : IDisposable
{
    /// <summary>
    /// Get a generic repository for any entity type
    /// </summary>
    /// <typeparam name="T">Entity type</typeparam>
    /// <returns>Repository instance</returns>
    IRepository<T> Repository<T>() where T : class;

    /// <summary>
    /// Save all changes to the database
    /// </summary>
    Task<Result<int>> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Begin a database transaction
    /// </summary>
    Task<Result> BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Commit the current transaction
    /// </summary>
    Task<Result> CommitTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rollback the current transaction
    /// </summary>
    Task<Result> RollbackTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes an action inside a single database transaction.
    /// Commits on success, rolls back on failure or exception.
    /// </summary>
    Task<Result> ExecuteInTransactionAsync(Func<Task<Result>> action, CancellationToken cancellationToken = default);
}
