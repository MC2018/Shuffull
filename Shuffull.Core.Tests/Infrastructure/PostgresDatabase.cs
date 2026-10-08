using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Shuffull.Core.Persistence;

namespace Shuffull.Core.Tests.Infrastructure;

/// <summary>
/// A throwaway Postgres database built by running the real migrations, so it also checks that they apply. Dropped
/// on dispose.
/// </summary>
public sealed class PostgresDatabase : IAsyncDisposable
{
    private static readonly TimeSpan LockWaitTimeout = TimeSpan.FromSeconds(10);

    private readonly string _adminConnectionString;
    private readonly string _name;

    public string ConnectionString { get; }

    private PostgresDatabase(string baseConnectionString)
    {
        _name = $"shuffull_test_{Guid.NewGuid():N}";
        _adminConnectionString = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = "postgres" }.ConnectionString;
        ConnectionString = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = _name }.ConnectionString;
    }

    public static async Task<PostgresDatabase> CreateAsync(string baseConnectionString)
    {
        var database = new PostgresDatabase(baseConnectionString);
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();
        return database;
    }

    public ShuffullContext CreateContext(params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<ShuffullContext>()
            .UseNpgsql(ConnectionString, npgsql => npgsql.MigrationsAssembly("Shuffull.Api"))
            .AddInterceptors(interceptors)
            .Options);

    public async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    /// <summary>
    /// Waits until some session in this database is blocked on a lock. Returns false if none is within the timeout.
    /// </summary>
    public async Task<bool> WaitForLockWaiterAsync()
    {
        await using var connection = await OpenConnectionAsync();
        var deadline = DateTime.UtcNow + LockWaitTimeout;
        while (DateTime.UtcNow < deadline)
        {
            await using var command = new NpgsqlCommand(
                "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'", connection);
            if ((long)(await command.ExecuteScalarAsync())! > 0)
            {
                return true;
            }

            await Task.Delay(50);
        }

        return false;
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_name}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
}
