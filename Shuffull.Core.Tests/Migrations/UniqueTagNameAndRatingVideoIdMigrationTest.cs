using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Migrations;

/// <summary>
/// The migration that makes Tags.Name and YoutubeRatingRequests.VideoId unique (Shuffull#43) first merges the
/// duplicates prod already holds, or creating the indexes would fail and the API would not start. Seeds the shapes
/// found in prod on 2026-10-29 one migration earlier, then applies it. Skipped unless SHUFFULL_TEST_POSTGRES is set.
/// </summary>
public class UniqueTagNameAndRatingVideoIdMigrationTest : IAsyncLifetime
{
    private const string PreviousMigration = "20261027134957_AddUserServerHashSalt";

    // ULIDs sort by creation time, so the "a" ids are the originals the migration keeps.
    private const string JazzId = "01a-jazz";
    private const string BigBandId = "01a-big-band";
    private const string BigBandCopyId = "01b-big-band";
    // A name in two types: the genre is kept even though the theme is older.
    private const string VocaloidThemeId = "01a-vocaloid";
    private const string VocaloidGenreId = "01b-vocaloid";

    private static readonly DateTime T0 = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SeededVersion = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

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

    /// <summary>
    /// Seeds with plain SQL naming only the columns that existed at <see cref="PreviousMigration"/>: today's EF model
    /// would write columns a later migration adds, which that schema doesn't have yet.
    /// </summary>
    private async Task SeedAsync(string sql)
    {
        await using var connection = await Database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    [PostgresFact]
    public async Task Migration_MergesDuplicateTagsAndRatingRows()
    {
        // Genre = 0 and Theme = 4 in the Type discriminator; Pending = 0 and Completed = 1, Like = 1 and Dislike = 2.
        await SeedAsync($"""
            INSERT INTO "Songs" ("SongId", "Name", "FileExtension", "FileHash", "ExternalSongId", "Exploratory",
                                 "LyricsInstrumental", "MetadataLocked", "TagAttempts", "TagRevision", "Version")
            SELECT id, id, '.mp3', id, 'video-' || id, false, false, false, 0, 0, '{SeededVersion:O}'
            FROM unnest(ARRAY['s1', 's2', 's3', 's4']) AS id;

            INSERT INTO "Tags" ("TagId", "Name", "Type") VALUES
                ('{JazzId}', 'Jazz', 0), ('{BigBandId}', 'Big Band', 0), ('{BigBandCopyId}', 'Big Band', 0),
                ('{VocaloidThemeId}', 'Vocaloid', 4), ('{VocaloidGenreId}', 'Vocaloid', 0);

            -- s1 carries both copies, as every affected song in prod did; s2 only the copy; s4 the older theme.
            INSERT INTO "SongTags" ("SongTagId", "SongId", "TagId") VALUES
                ('l1', 's1', '{BigBandId}'), ('l2', 's1', '{BigBandCopyId}'), ('l3', 's2', '{BigBandCopyId}'),
                ('l4', 's3', '{JazzId}'), ('l5', 's4', '{VocaloidThemeId}');

            INSERT INTO "GenreRelations" ("GenreRelationId", "MainGenreId", "SubGenreId") VALUES
                ('r1', '{JazzId}', '{BigBandId}'), ('r2', '{JazzId}', '{BigBandCopyId}'), ('r3', '{JazzId}', '{VocaloidGenreId}');

            INSERT INTO "YoutubeRatingRequests"
                ("YoutubeRatingRequestId", "SongId", "VideoId", "Rating", "Status", "CreatedAt", "UpdatedAt") VALUES
                -- s1: a pending row beats a newer completed one, since it still has work to do.
                ('s1-old-pending', 's1', 'video-s1', 2, 0, '{T0:O}', '{T0:O}'),
                ('s1-new-done', 's1', 'video-s1', 1, 1, '{T0.AddMinutes(1):O}', '{T0.AddMinutes(1):O}'),
                -- s2: all completed, so the most recent wins.
                ('s2-old', 's2', 'video-s2', 1, 1, '{T0:O}', '{T0:O}'),
                ('s2-new', 's2', 'video-s2', 2, 1, '{T0.AddMinutes(1):O}', '{T0.AddMinutes(1):O}'),
                ('s3-only', 's3', 'video-s3', 1, 1, '{T0:O}', '{T0:O}');
            """);

        await using (var migrate = Database.CreateContext())
        {
            await migrate.GetService<IMigrator>().MigrateAsync();
        }

        await using var check = Database.CreateContext();
        Assert.Equal([BigBandId, JazzId, VocaloidGenreId], await check.Tags.Select(t => t.TagId).OrderBy(id => id).ToListAsync());
        Assert.Equal(
            ["s1", "s2"],
            await check.SongTags.Where(st => st.TagId == BigBandId).Select(st => st.SongId).OrderBy(id => id).ToListAsync());
        Assert.Equal(VocaloidGenreId, (await check.SongTags.SingleAsync(st => st.SongId == "s4")).TagId);
        Assert.Equal(4, await check.SongTags.CountAsync());
        Assert.Equal(
            [("r1", BigBandId), ("r3", VocaloidGenreId)],
            (await check.GenreRelations.OrderBy(r => r.GenreRelationId).ToListAsync()).Select(r => (r.GenreRelationId, r.SubGenreId)));
        Assert.Equal(
            ["s1-old-pending", "s2-new", "s3-only"],
            await check.YoutubeRatingRequests.Select(r => r.YoutubeRatingRequestId).OrderBy(id => id).ToListAsync());

        // Songs whose links moved get a new Version each, so the app re-reads their tags; s3's links didn't move.
        var versions = await check.Songs.ToDictionaryAsync(s => s.SongId, s => s.Version);
        Assert.Equal(SeededVersion, versions["s3"]);
        Assert.All(new[] { "s1", "s2", "s4" }, id => Assert.True(versions[id] > SeededVersion));
        Assert.Equal(3, new[] { versions["s1"], versions["s2"], versions["s4"] }.Distinct().Count());
    }
}
