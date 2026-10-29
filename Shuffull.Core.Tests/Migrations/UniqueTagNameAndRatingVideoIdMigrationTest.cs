using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;
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

    private static Song NewSong(string id) => new()
    {
        SongId = id,
        Name = id,
        FileExtension = ".mp3",
        FileHash = id,
        ExternalSongId = $"video-{id}",
        Version = DateTime.UtcNow,
    };

    private static YoutubeRatingRequest Rating(string id, string songId, YoutubeRatingStatus status, YoutubeRating rating, DateTime updatedAt) => new()
    {
        YoutubeRatingRequestId = id,
        SongId = songId,
        VideoId = $"video-{songId}",
        Rating = rating,
        Status = status,
        CreatedAt = updatedAt,
        UpdatedAt = updatedAt,
    };

    private static SongTag Link(string songId, string tagId) => new() { SongTagId = $"{songId}-{tagId}", SongId = songId, TagId = tagId };

    [PostgresFact]
    public async Task Migration_MergesDuplicateTagsAndRatingRows()
    {
        var t0 = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        await using (var seed = Database.CreateContext())
        {
            seed.Songs.AddRange(NewSong("s1"), NewSong("s2"), NewSong("s3"));
            seed.Tags.AddRange(
                new Genre { TagId = JazzId, Name = "Jazz" },
                new Genre { TagId = BigBandId, Name = "Big Band" },
                new Genre { TagId = BigBandCopyId, Name = "Big Band" });
            // s1 carries both copies, as every affected song in prod did; s2 only the copy.
            seed.SongTags.AddRange(Link("s1", BigBandId), Link("s1", BigBandCopyId), Link("s2", BigBandCopyId));
            seed.GenreRelations.AddRange(
                new GenreRelation { GenreRelationId = "r1", MainGenreId = JazzId, SubGenreId = BigBandId },
                new GenreRelation { GenreRelationId = "r2", MainGenreId = JazzId, SubGenreId = BigBandCopyId });
            seed.YoutubeRatingRequests.AddRange(
                // s1: a pending row beats a newer completed one, since it still has work to do.
                Rating("s1-old-pending", "s1", YoutubeRatingStatus.Pending, YoutubeRating.Dislike, t0),
                Rating("s1-new-done", "s1", YoutubeRatingStatus.Completed, YoutubeRating.Like, t0.AddMinutes(1)),
                // s2: all completed, so the most recent wins.
                Rating("s2-old", "s2", YoutubeRatingStatus.Completed, YoutubeRating.Like, t0),
                Rating("s2-new", "s2", YoutubeRatingStatus.Completed, YoutubeRating.Dislike, t0.AddMinutes(1)),
                Rating("s3-only", "s3", YoutubeRatingStatus.Completed, YoutubeRating.Like, t0));
            await seed.SaveChangesAsync();
        }

        await using (var migrate = Database.CreateContext())
        {
            await migrate.GetService<IMigrator>().MigrateAsync();
        }

        await using var check = Database.CreateContext();
        Assert.Equal([BigBandId, JazzId], await check.Tags.Select(t => t.TagId).OrderBy(id => id).ToListAsync());
        Assert.Equal(
            ["s1", "s2"],
            await check.SongTags.Where(st => st.TagId == BigBandId).Select(st => st.SongId).OrderBy(id => id).ToListAsync());
        Assert.Equal(2, await check.SongTags.CountAsync());
        var relation = Assert.Single(await check.GenreRelations.ToListAsync());
        Assert.Equal(BigBandId, relation.SubGenreId);
        Assert.Equal(
            ["s1-old-pending", "s2-new", "s3-only"],
            await check.YoutubeRatingRequests.Select(r => r.YoutubeRatingRequestId).OrderBy(id => id).ToListAsync());
    }
}
