using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shuffull.Core.Persistence;
using Shuffull.Core.Persistence.Repositories;

namespace Shuffull.Core.Tests.Infrastructure;

/// <summary>
/// Test fixture that provides a real in-memory SQLite database backed by the concrete
/// <see cref="ShuffullContext"/>. The schema is created from the EF model (EnsureCreated), not the
/// SQL Server migrations, so it stays provider-agnostic. Use as a field in a test class and dispose
/// after each test.
/// </summary>
public class DatabaseFixture : IDisposable
{
    private readonly SqliteConnection _connection;

    public ShuffullContext Context { get; }
    public IUnitOfWork UnitOfWork { get; }

    public DatabaseFixture()
    {
        // Keep the connection open for the fixture's lifetime — an in-memory SQLite database only
        // exists while at least one connection to it is open.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ShuffullContext>()
            .UseSqlite(_connection)
            .Options;

        Context = new ShuffullContext(options);
        Context.Database.EnsureCreated();

        UnitOfWork = new UnitOfWork(Context, NullLogger<UnitOfWork>.Instance);
    }

    public void Dispose()
    {
        UnitOfWork?.Dispose();
        Context?.Dispose();
        _connection?.Close();
        _connection?.Dispose();
        GC.SuppressFinalize(this);
    }
}
