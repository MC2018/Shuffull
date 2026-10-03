using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Persistence.Specifications;

namespace Shuffull.Core.Persistence.Repositories;

/// <summary>
/// Generic repository implementation using EF Core. Works over the base <see cref="DbContext"/>
/// so Core stays decoupled from the concrete ShuffullContext.
/// </summary>
/// <typeparam name="T">Entity type</typeparam>
public class Repository<T> : IRepository<T> where T : class
{
    private readonly DbContext _context;
    private readonly DbSet<T> _dbSet;

    public Repository(DbContext context)
    {
        _context = context;
        _dbSet = context.Set<T>();
    }

    public virtual async Task<Result<T>> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var entity = await _dbSet.FindAsync(new object[] { id }, cancellationToken);

            if (entity == null)
            {
                return Result.Error<T>($"Entity with ID {id} not found.");
            }

            return Result.Ok(entity);
        }
        catch (Exception ex)
        {
            return Result.Error<T>($"Error retrieving entity by ID: {ex.Message}");
        }
    }

    public async Task<Result<T>> GetAsync(ISpecification<T> spec, CancellationToken cancellationToken = default)
    {
        try
        {
            var entity = await ApplySpecification(spec).FirstOrDefaultAsync(cancellationToken);

            if (entity == null)
            {
                return Result.Error<T>("Entity not found.");
            }

            return Result.Ok(entity);
        }
        catch (Exception ex)
        {
            return Result.Error<T>($"Error retrieving entity: {ex.Message}");
        }
    }

    public async Task<Result<List<T>>> ListAsync(ISpecification<T> spec, CancellationToken cancellationToken = default)
    {
        try
        {
            var entities = await ApplySpecification(spec).ToListAsync(cancellationToken);
            return Result.Ok(entities);
        }
        catch (Exception ex)
        {
            return Result.Error<List<T>>($"Error retrieving entities: {ex.Message}");
        }
    }

    public async Task<Result<List<T>>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var entities = await _dbSet.ToListAsync(cancellationToken);
            return Result.Ok(entities);
        }
        catch (Exception ex)
        {
            return Result.Error<List<T>>($"Error retrieving all entities: {ex.Message}");
        }
    }

    public async Task<Result<int>> CountAsync(ISpecification<T> spec, CancellationToken cancellationToken = default)
    {
        try
        {
            var count = await ApplySpecification(spec).CountAsync(cancellationToken);
            return Result.Ok(count);
        }
        catch (Exception ex)
        {
            return Result.Error<int>($"Error counting entities: {ex.Message}");
        }
    }

    public async Task<Result<bool>> AnyAsync(ISpecification<T> spec, CancellationToken cancellationToken = default)
    {
        try
        {
            var exists = await ApplySpecification(spec).AnyAsync(cancellationToken);
            return Result.Ok(exists);
        }
        catch (Exception ex)
        {
            return Result.Error<bool>($"Error checking entity existence: {ex.Message}");
        }
    }

    public async Task<Result<T>> AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        try
        {
            await _dbSet.AddAsync(entity, cancellationToken);
            return Result.Ok(entity);
        }
        catch (Exception ex)
        {
            return Result.Error<T>($"Error adding entity: {ex.Message}");
        }
    }

    public Result Update(T entity)
    {
        try
        {
            _dbSet.Update(entity);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Error($"Error updating entity: {ex.Message}");
        }
    }

    public Result Delete(T entity)
    {
        try
        {
            _dbSet.Remove(entity);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Error($"Error deleting entity: {ex.Message}");
        }
    }

    public Result DeleteRange(IEnumerable<T> entities)
    {
        try
        {
            _dbSet.RemoveRange(entities);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Error($"Error deleting entities: {ex.Message}");
        }
    }

    /// <summary>
    /// Apply specification to queryable
    /// </summary>
    private IQueryable<T> ApplySpecification(ISpecification<T> spec)
    {
        return spec.Apply(_dbSet.AsQueryable());
    }
}
