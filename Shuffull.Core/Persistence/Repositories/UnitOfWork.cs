using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Nut.Results;

namespace Shuffull.Core.Persistence.Repositories;

/// <summary>
/// Unit of Work implementation coordinating repositories and database transactions.
/// Resolves the request-scoped <see cref="DbContext"/> (registered in the host as the concrete
/// ShuffullContext), keeping Core independent of the concrete context type.
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly DbContext _context;
    private readonly ILogger<UnitOfWork> _logger;
    private readonly Dictionary<Type, object> _repositories;
    private IDbContextTransaction? _transaction;

    public UnitOfWork(DbContext context, ILogger<UnitOfWork> logger)
    {
        _context = context;
        _logger = logger;
        _repositories = new Dictionary<Type, object>();
    }

    /// <summary>
    /// Get or create a repository for the specified entity type
    /// </summary>
    public IRepository<T> Repository<T>() where T : class
    {
        var type = typeof(T);

        if (!_repositories.ContainsKey(type))
        {
            var repositoryType = typeof(Repository<>).MakeGenericType(type);
            var repositoryInstance = Activator.CreateInstance(repositoryType, _context);

            if (repositoryInstance == null)
            {
                // Catastrophic failure - should not happen.
                throw new InvalidOperationException($"Failed to create repository for type {type.Name}");
            }

            _repositories[type] = repositoryInstance;
        }

        return (IRepository<T>)_repositories[type];
    }

    public async Task<Result<int>> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var count = await _context.SaveChangesAsync(cancellationToken);
            return Result.Ok(count);
        }
        catch (Exception ex)
        {
            return Result.Error<int>($"Error saving changes: {ex.Message}");
        }
    }

    public async Task<Result> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Error($"Error beginning transaction: {ex.Message}");
        }
    }

    public async Task<Result> CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction == null)
        {
            return Result.Error("No transaction has been started.");
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            await _transaction.CommitAsync(cancellationToken);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            await RollbackTransactionAsync(cancellationToken);
            return Result.Error($"Error committing transaction: {ex.Message}");
        }
        finally
        {
            _transaction.Dispose();
            _transaction = null;
        }
    }

    public virtual async Task<Result> RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction == null)
        {
            return Result.Error("No transaction has been started.");
        }

        try
        {
            await _transaction.RollbackAsync(cancellationToken);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Error($"Error rolling back transaction: {ex.Message}");
        }
        finally
        {
            _transaction.Dispose();
            _transaction = null;
        }
    }

    public async Task<Result> ExecuteInTransactionAsync(Func<Task<Result>> action, CancellationToken cancellationToken = default)
    {
        var beginResult = await BeginTransactionAsync(cancellationToken);
        if (beginResult.IsError) return beginResult;

        try
        {
            var actionResult = await action();
            if (actionResult.IsError)
            {
                return await RollbackAndLogAsync(actionResult.GetError().Message, cancellationToken);
            }

            return await CommitTransactionAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            return await RollbackAndLogAsync($"Transaction failed: {ex.Message}", cancellationToken);
        }
    }

    private async Task<Result> RollbackAndLogAsync(string originalError, CancellationToken cancellationToken)
    {
        var rollbackResult = await RollbackTransactionAsync(cancellationToken);
        if (rollbackResult.IsError)
        {
            var rollbackError = rollbackResult.GetError().Message;
            _logger.LogCritical("Transaction rollback failed after action error. OriginalError={OriginalError} RollbackError={RollbackError}", originalError, rollbackError);
            return Result.Error($"Action failed: {originalError}; Rollback also failed: {rollbackError}");
        }
        return Result.Error(originalError);
    }

    public void Dispose()
    {
        _transaction?.Dispose();
        // Note: the DbContext lifetime is owned by DI (it is the scoped ShuffullContext), so it is
        // intentionally NOT disposed here — disposing it would break other scoped consumers.
        GC.SuppressFinalize(this);
    }
}
