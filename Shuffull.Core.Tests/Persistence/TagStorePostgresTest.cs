using Microsoft.EntityFrameworkCore;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Persistence;

/// <summary>
/// Two writers creating the same new tag name at once, against real Postgres where their transactions really overlap
/// (Shuffull#82). The second waits on the first's uncommitted row instead of failing the unique index on Tags.Name.
/// Skipped unless SHUFFULL_TEST_POSTGRES is set.
/// </summary>
public class TagStorePostgresTest : IAsyncLifetime
{
    private PostgresDatabase? _database;

    private PostgresDatabase Database => _database ?? throw new InvalidOperationException("Postgres is not configured.");

    public async Task InitializeAsync()
    {
        if (PostgresFactAttribute.BaseConnectionString is { Length: > 0 } baseConnectionString)
        {
            _database = await PostgresDatabase.CreateAsync(baseConnectionString);
        }
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }

    [PostgresFact]
    public async Task SameNewName_InAnOpenTransaction_SecondWriterWaitsThenGetsTheFirstWritersTag()
    {
        await using var first = Database.CreateContext();
        await using var firstTransaction = await first.Database.BeginTransactionAsync();
        await TagStore.GetOrCreateAsync(first, [new Language { TagId = "first", Name = "Klingon" }], CancellationToken.None);

        await using var second = Database.CreateContext();
        await using var secondTransaction = await second.Database.BeginTransactionAsync();
        var secondWrite = TagStore.GetOrCreateAsync(second, [new Language { TagId = "second", Name = "Klingon" }], CancellationToken.None);

        Assert.True(await Database.WaitForLockWaiterAsync());
        Assert.False(secondWrite.IsCompleted);
        await firstTransaction.CommitAsync();

        var tags = await secondWrite;
        await secondTransaction.CommitAsync();

        Assert.Equal("first", Assert.Single(tags).TagId);
        await using var check = Database.CreateContext();
        Assert.Equal("first", (await check.Tags.SingleAsync(t => t.Name == "Klingon")).TagId);
    }

    [PostgresFact]
    public async Task SameNewName_FirstWriterRollsBack_SecondWriterCreatesIt()
    {
        await using var first = Database.CreateContext();
        await using var firstTransaction = await first.Database.BeginTransactionAsync();
        await TagStore.GetOrCreateAsync(first, [new Language { TagId = "first", Name = "Klingon" }], CancellationToken.None);

        await using var second = Database.CreateContext();
        await using var secondTransaction = await second.Database.BeginTransactionAsync();
        var secondWrite = TagStore.GetOrCreateAsync(second, [new Language { TagId = "second", Name = "Klingon" }], CancellationToken.None);

        Assert.True(await Database.WaitForLockWaiterAsync());
        await firstTransaction.RollbackAsync();

        var tags = await secondWrite;
        await secondTransaction.CommitAsync();

        Assert.Equal("second", Assert.Single(tags).TagId);
    }

    [PostgresFact]
    public async Task ExistingAndNewNames_ReturnsOneStoredTagPerName()
    {
        await using (var seed = Database.CreateContext())
        {
            seed.Tags.Add(new Genre { TagId = "rock", Name = "Rock" });
            await seed.SaveChangesAsync();
        }

        await using var context = Database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var tags = await TagStore.GetOrCreateAsync(context,
            [new Genre { TagId = "rock-again", Name = "Rock" }, new Language { TagId = "klingon", Name = "Klingon" }],
            CancellationToken.None);
        await transaction.CommitAsync();

        Assert.Equal(["klingon", "rock"], tags.Select(t => t.TagId).Order());
        await using var check = Database.CreateContext();
        Assert.Equal(2, await check.Tags.CountAsync());
    }
}
