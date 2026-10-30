using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Nut.Results;
using Shuffull.Core.Features.Songs.GetSongsChanged;
using Shuffull.Core.Features.UserSongs.GetUserSongs;
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
public class SyncKeysetPagingPostgresTest : IAsyncLifetime
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

    /// <summary>Seeds 1,200 songs, each in the user's library, then stamps every row with one shared now().</summary>
    private async Task<(string UserId, HashSet<string> SongIds)> SeedTieAsync()
    {
        var user = new User { UserId = Guid.NewGuid().ToString(), Username = "alice", Version = DateTime.UtcNow, ServerHash = "h" };
        var songIds = Enumerable.Range(0, 1200).Select(_ => Guid.NewGuid().ToString()).ToHashSet();
        await using (var context = Database.CreateContext())
        {
            context.Users.Add(user);
            context.Songs.AddRange(songIds.Select(id => new Song { SongId = id, Name = "Song", FileExtension = ".mp3", FileHash = id, Version = DateTime.UtcNow }));
            context.UserSongs.AddRange(songIds.Select(id => new UserSong { UserId = user.UserId, SongId = id, LastPlayed = DateTime.UtcNow, Version = DateTime.UtcNow }));
            await context.SaveChangesAsync();
        }

        await using var connection = await Database.OpenConnectionAsync();
        await using var stamp = connection.CreateCommand();
        stamp.CommandText = """
            UPDATE "Songs" SET "Version" = now();
            UPDATE "UserSongs" SET "Version" = now();
            """;
        await stamp.ExecuteNonQueryAsync();
        return (user.UserId, songIds);
    }

    [PostgresFact]
    public async Task SongsChanged_KeysetCursor_PagesThroughAOneTimestampTie_EachSongOnce()
    {
        var (_, songIds) = await SeedTieAsync();

        var pages = new List<GetSongsChangedResponse>();
        var query = new GetSongsChangedQuery(DateTime.MinValue);
        while (pages.Count < 10)
        {
            await using var context = Database.CreateContext();
            var handler = new GetSongsChangedHandler(new UnitOfWork(context, NullLogger<UnitOfWork>.Instance), new TagStalenessJudge(new ModelStrengths(), new ConfigurationBuilder().Build()));
            var page = (await handler.Handle(query, CancellationToken.None)).Get();
            pages.Add(page);
            if (page.EndOfList)
            {
                break;
            }

            query = new GetSongsChangedQuery(page.Songs[^1].Version, page.Songs[^1].SongId);
        }

        Assert.Equal(new[] { 500, 500, 200 }, pages.Select(p => p.Songs.Count).ToArray());
        var returned = pages.SelectMany(p => p.Songs).Select(s => s.SongId).ToList();
        Assert.Equal(returned.Count, returned.Distinct().Count());
        Assert.True(songIds.SetEquals(returned));
        Assert.Single(pages.SelectMany(p => p.Songs).Select(s => s.Version).Distinct());
    }

    [PostgresFact]
    public async Task UserSongs_KeysetCursor_PagesThroughAOneTimestampTie_EachRecordOnce()
    {
        var (userId, songIds) = await SeedTieAsync();

        var pages = new List<GetUserSongsResponse>();
        var query = new GetUserSongsQuery(userId, DateTime.MinValue);
        while (pages.Count < 10)
        {
            await using var context = Database.CreateContext();
            var page = (await new GetUserSongsHandler(new UnitOfWork(context, NullLogger<UnitOfWork>.Instance)).Handle(query, CancellationToken.None)).Get();
            pages.Add(page);
            if (page.EndOfList)
            {
                break;
            }

            query = new GetUserSongsQuery(userId, page.UserSongs[^1].Version, page.UserSongs[^1].SongId);
        }

        Assert.Equal(new[] { 500, 500, 200 }, pages.Select(p => p.UserSongs.Count).ToArray());
        var returned = pages.SelectMany(p => p.UserSongs).Select(us => us.SongId).ToList();
        Assert.Equal(returned.Count, returned.Distinct().Count());
        Assert.True(songIds.SetEquals(returned));
    }
}
