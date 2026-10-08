namespace Shuffull.Core.Tests.Infrastructure;

/// <summary>
/// A [Fact] that runs only when <see cref="ConnectionStringVariable"/> holds a Postgres connection string (without a
/// database), e.g. <c>Host=127.0.0.1;Port=5432;Username=postgres;Password=pw</c>. Otherwise it is reported as
/// skipped. For behaviour SQLite can't show: row locks, and foreign keys racing across connections.
/// </summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public const string ConnectionStringVariable = "SHUFFULL_TEST_POSTGRES";

    public static string? BaseConnectionString => Environment.GetEnvironmentVariable(ConnectionStringVariable);

    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(BaseConnectionString))
        {
            Skip = $"Set {ConnectionStringVariable} to a Postgres connection string (no database) to run this test.";
        }
    }
}
