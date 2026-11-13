using Nut.Results;
using Shuffull.Core.Features.Songs.GetDeletedSongs;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Features.Songs;

/// <summary>
/// The deleted-songs cursor against real Postgres, which stores <c>timestamptz</c> to the microsecond while a .NET
/// <see cref="DateTime"/> carries 100ns ticks. The cursor is built from the value read back, so it must still page
/// through one purge (one shared DeletedAt) without skipping or repeating. Skipped unless SHUFFULL_TEST_POSTGRES is set.
/// </summary>
public class GetDeletedSongsHandlerPostgresTest : IAsyncLifetime
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
    public async Task PagesThroughOnePurge_WithSubMicrosecondDeletedAt()
    {
        // 7 ticks past a microsecond: Postgres keeps only the microsecond, like a real purge's DateTime.UtcNow.
        var purgedAt = new DateTime(2026, 11, 5, 18, 30, 0, DateTimeKind.Utc).AddTicks(1_234_567);
        await using (var seed = Database.CreateContext())
        {
            foreach (var id in new[] { "p1", "p2", "p3", "p4", "p5" })
            {
                seed.SongTombstones.Add(Tombstone(id, "user-a", purgedAt));
            }
            seed.SongTombstones.Add(Tombstone("e1", "user-a", purgedAt.AddDays(-1)));
            seed.SongTombstones.Add(Tombstone("x1", "user-b", purgedAt));
            await seed.SaveChangesAsync();
        }

        var seen = new List<string>();
        string? before = null;
        var pages = 0;
        do
        {
            await using var context = Database.CreateContext();
            var page = (await new GetDeletedSongsHandler(context)
                .Handle(new GetDeletedSongsQuery("user-a", before, Limit: 2), CancellationToken.None)).Get();
            seen.AddRange(page.Songs.Select(s => s.SongId));
            before = page.NextBefore;
            pages++;
        } while (before is not null && pages < 10);

        Assert.Equal(["p5", "p4", "p3", "p2", "p1", "e1"], seen);
    }

    private static SongTombstone Tombstone(string songId, string userId, DateTime deletedAt) => new()
    {
        SongId = songId,
        Name = songId,
        FileHash = $"hash-{songId}",
        FileExtension = ".mp3",
        DeletedByUserId = userId,
        DeletedAt = deletedAt,
    };
}
