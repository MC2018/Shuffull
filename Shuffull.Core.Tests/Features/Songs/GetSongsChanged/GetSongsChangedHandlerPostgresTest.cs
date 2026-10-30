using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Nut.Results;
using Shuffull.Core.Features.Songs.GetSongsChanged;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence.Repositories;
using Shuffull.Core.Tests.Infrastructure;
using Shuffull.Core.Tools;
using Shuffull.Metadata.Tools;

namespace Shuffull.Core.Tests.Features.Songs.GetSongsChanged;

/// <summary>
/// Keyset paging against real Postgres, where it runs in prod: <c>timestamptz</c> keeps microseconds, and the id
/// comparison and the ORDER BY both use the column's collation, not ordinal order. Seeds a tie the way
/// shuffull-workspace#38 hit it: one <c>now()</c> on every row, with GUID ids. Skipped unless SHUFFULL_TEST_POSTGRES
/// is set.
/// </summary>
public class GetSongsChangedHandlerPostgresTest : IAsyncLifetime
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
    public async Task KeysetCursor_PagesThroughAOneTimestampTie_EachSongOnce()
    {
        // Arrange — 1,200 songs, then one shared now() on every row.
        var songIds = Enumerable.Range(0, 1200).Select(_ => Guid.NewGuid().ToString()).ToHashSet();
        await using (var context = Database.CreateContext())
        {
            context.Songs.AddRange(songIds.Select(id => new Song { SongId = id, Name = "Song", FileExtension = ".mp3", FileHash = id, Version = DateTime.UtcNow }));
            await context.SaveChangesAsync();
        }
        await using (var connection = await Database.OpenConnectionAsync())
        await using (var stamp = connection.CreateCommand())
        {
            stamp.CommandText = """UPDATE "Songs" SET "Version" = now();""";
            await stamp.ExecuteNonQueryAsync();
        }

        // Act — page from the last song's exact Version and SongId, each page on a fresh context as a request is.
        var pages = new List<GetSongsChangedResponse>();
        var query = new GetSongsChangedQuery(DateTime.MinValue);
        while (pages.Count < 10)
        {
            await using var context = Database.CreateContext();
            var handler = new GetSongsChangedHandler(
                new UnitOfWork(context, NullLogger<UnitOfWork>.Instance),
                new TagStalenessJudge(new ModelStrengths(), new ConfigurationBuilder().Build()));
            var page = (await handler.Handle(query, CancellationToken.None)).Get();
            pages.Add(page);
            if (page.EndOfList)
            {
                break;
            }

            query = new GetSongsChangedQuery(page.Songs[^1].Version, page.Songs[^1].SongId);
        }

        // Assert
        Assert.Equal(new[] { 500, 500, 200 }, pages.Select(p => p.Songs.Count).ToArray());
        var returned = pages.SelectMany(p => p.Songs).Select(s => s.SongId).ToList();
        Assert.Equal(returned.Count, returned.Distinct().Count());
        Assert.True(songIds.SetEquals(returned));
        Assert.Single(pages.SelectMany(p => p.Songs).Select(s => s.Version).Distinct());
    }
}
