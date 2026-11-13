using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Migrations;

/// <summary>
/// The migration that keys SongArtists by (SongId, ArtistId) (Shuffull#5) first deletes any pair linked twice, or
/// adding the key would fail and the API would not start. Its Down gives every row a fresh SongArtistId, so a rollback
/// can rebuild the old key. Skipped unless SHUFFULL_TEST_POSTGRES is set.
/// </summary>
public class RemoveSongArtistIdMigrationTest : IAsyncLifetime
{
    private const string PreviousMigration = "20261102083959_AddUserSongKeptAt";

    private PostgresDatabase? _database;

    private PostgresDatabase Database => _database ?? throw new InvalidOperationException("Postgres is not configured.");

    public async Task InitializeAsync()
    {
        if (PostgresFactAttribute.BaseConnectionString is { Length: > 0 } baseConnectionString)
        {
            _database = await PostgresDatabase.CreateAsync(baseConnectionString, PreviousMigration);
        }
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }

    private async Task<List<string>> QueryAsync(string sql)
    {
        await using var connection = await Database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetString(0));
        }
        return rows;
    }

    private async Task MigrateAsync(string? target = null)
    {
        await using var context = Database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(target);
    }

    [PostgresFact]
    public async Task Migration_KeepsOneRowPerPair_AndDownRebuildsTheOldKey()
    {
        // Plain SQL naming only the columns that exist at PreviousMigration: today's model has no SongArtistId.
        await QueryAsync("""
            INSERT INTO "Songs" ("SongId", "Name", "FileExtension", "FileHash", "Exploratory",
                                 "LyricsInstrumental", "MetadataLocked", "TagAttempts", "TagRevision", "Version")
            SELECT id, id, '.mp3', id, false, false, false, 0, 0, now()
            FROM unnest(ARRAY['s1', 's2']) AS id;

            INSERT INTO "Artists" ("ArtistId", "Name") VALUES ('a1', 'A'), ('a2', 'B');

            -- s1 links a1 twice; every other pair once.
            INSERT INTO "SongArtists" ("SongArtistId", "SongId", "ArtistId") VALUES
                ('01a', 's1', 'a1'), ('01b', 's1', 'a1'), ('01c', 's1', 'a2'), ('01d', 's2', 'a1');
            """);

        await MigrateAsync();

        await using (var check = Database.CreateContext())
        {
            Assert.Equal(
                ["s1:a1", "s1:a2", "s2:a1"],
                (await check.SongArtists.ToListAsync()).Select(sa => $"{sa.SongId}:{sa.ArtistId}").Order());
        }
        Assert.Equal(
            ["SongId", "ArtistId"],
            await QueryAsync("""
                SELECT a.attname FROM pg_index i
                JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY(i.indkey)
                WHERE i.indrelid = '"SongArtists"'::regclass AND i.indisprimary
                ORDER BY array_position(i.indkey::int2[], a.attnum);
                """));

        await MigrateAsync(PreviousMigration);

        Assert.Equal(
            ["s1:a1", "s1:a2", "s2:a1"],
            await QueryAsync("""SELECT "SongArtistId" FROM "SongArtists" ORDER BY 1;"""));
    }
}
