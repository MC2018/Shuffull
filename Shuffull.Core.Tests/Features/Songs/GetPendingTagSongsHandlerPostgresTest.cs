using Microsoft.Extensions.Configuration;
using Nut.Results;
using Shuffull.Core.Features.Songs.GetPendingTagSongs;
using Shuffull.Core.Features.Songs.HoldTagSongs;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Tests.Infrastructure;
using Shuffull.Metadata.Services.AI;
using Shuffull.Metadata.Tools;

namespace Shuffull.Core.Tests.Features.Songs;

/// <summary>
/// The tagging queue against real Postgres, on the real migrations. SQLite can't show how the pending set (an IN over
/// a UNION ALL of the two tiers) and the backoff (bulk UPDATEs) translate for Npgsql, which is what prod runs.
/// Skipped unless SHUFFULL_TEST_POSTGRES is set.
/// </summary>
public class GetPendingTagSongsHandlerPostgresTest : IAsyncLifetime
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

    private static readonly ModelStrengths Strengths = new(new Dictionary<string, int>
    {
        ["weak-model"] = 10,
        ["strong-model"] = 30,
        ["frontier-model"] = 50,
    });

    private static readonly IConfiguration Config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["AI:OpenAI:WeakModelName"] = "weak-model",
        ["AI:OpenAI:StrongModelName"] = "strong-model",
    }).Build();

    private async Task<Song> SeedSongAsync(DateTime version, string? tagModel = null, string? likedBy = null,
                                           int tagRevision = TagRevision.Current, bool locked = false)
    {
        await using var context = Database.CreateContext();
        var song = new Song
        {
            SongId = Guid.NewGuid().ToString(),
            Name = $"song-{Guid.NewGuid():N}",
            FileExtension = ".mp3",
            FileHash = Guid.NewGuid().ToString(),
            TagModel = tagModel,
            TagRevision = tagRevision,
            MetadataLocked = locked,
            Version = version,
        };
        context.Songs.Add(song);
        if (likedBy is not null)
        {
            context.UserSongs.Add(new UserSong
            {
                UserId = likedBy, SongId = song.SongId, LikeStatus = LikeStatus.Like,
                Version = DateTime.UtcNow, LastPlayed = DateTime.UtcNow,
            });
        }
        await context.SaveChangesAsync();
        return song;
    }

    private async Task<PendingTagSongsResponse> RunAsync(int limit)
    {
        await using var context = Database.CreateContext();
        return (await new GetPendingTagSongsHandler(context, Strengths, Config, new RecordingHealthSignals(), TimeProvider.System)
            .Handle(new GetPendingTagSongsQuery(limit), CancellationToken.None)).Get();
    }

    [PostgresFact]
    public async Task BothTiersAreOffered_AndAnUntaggedSongBacksOff()
    {
        await using (var context = Database.CreateContext())
        {
            context.Users.Add(new User { UserId = "liker", Username = $"user-{Guid.NewGuid():N}", Version = DateTime.UtcNow, ServerHash = "h" });
            await context.SaveChangesAsync();
        }

        var day = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var liked = await SeedSongAsync(day, tagModel: "weak-model", likedBy: "liker"); // strong tier, behind
        var untagged = await SeedSongAsync(day.AddDays(1));                               // weak tier, behind
        await SeedSongAsync(day.AddDays(2), tagModel: "weak-model");                      // weak tier, done
        var newest = await SeedSongAsync(day.AddDays(3));                                 // weak tier, behind

        var first = await RunAsync(limit: 2);
        Assert.Equal([liked.SongId, untagged.SongId], first.Songs.Select(s => s.SongId));
        Assert.Equal([TagTiers.Strong, TagTiers.Weak], first.Songs.Select(s => s.Tier));
        Assert.Equal(3, first.Remaining);

        // Neither was tagged. Both sit out their backoff, so the next page reaches the song behind them.
        var second = await RunAsync(limit: 2);
        Assert.Equal([newest.SongId], second.Songs.Select(s => s.SongId));
        Assert.Equal(3, second.Remaining);

        await using var check = Database.CreateContext();
        var reloaded = await check.Songs.FindAsync(liked.SongId);
        Assert.Equal(1, reloaded!.TagAttempts);
        Assert.True(reloaded.NextTagAttemptAt > DateTime.UtcNow);
        Assert.Equal(day, reloaded.Version);
    }

    [PostgresFact]
    public async Task OlderRevisions_AreOffered_OnATierNoWeakerThanTheirTags()
    {
        // The revision branch and the tier projection both carry a model-name list into SQL. SQLite translates
        // those differently, so pin the Postgres shape too (shuffull-workspace#41).
        var day = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var weakTagged = await SeedSongAsync(day, tagModel: "weak-model", tagRevision: TagRevision.Current - 1);
        var strongTagged = await SeedSongAsync(day.AddDays(1), tagModel: "STRONG-model", tagRevision: TagRevision.Current - 1);
        await SeedSongAsync(day.AddDays(2), tagModel: "weak-model", tagRevision: TagRevision.Current - 1, locked: true);
        await SeedSongAsync(day.AddDays(3), tagModel: "weak-model");

        var page = await RunAsync(limit: 10);

        Assert.Equal([weakTagged.SongId, strongTagged.SongId], page.Songs.Select(s => s.SongId));
        Assert.Equal([TagTiers.Weak, TagTiers.Strong], page.Songs.Select(s => s.Tier));
        Assert.Equal(2, page.Remaining);
    }

    [PostgresFact]
    public async Task RealUpgrades_ComeFirst_AndASongNoTierCanWriteIsNotOffered()
    {
        var day = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var rescore = await SeedSongAsync(day, tagModel: "weak-model", tagRevision: TagRevision.Current - 1);
        await SeedSongAsync(day.AddDays(1), tagModel: "frontier-model", tagRevision: TagRevision.Current - 1);
        var untagged = await SeedSongAsync(day.AddDays(2));

        var page = await RunAsync(limit: 10);

        Assert.Equal([untagged.SongId, rescore.SongId], page.Songs.Select(s => s.SongId));
        Assert.Equal(2, page.Remaining);
    }

    [PostgresFact]
    public async Task AHoldReport_BringsTheSongBack_AndOnlyEverBringsADueTimeForward()
    {
        // The hold's UPDATE carries a conditional SET (keep the earlier due time); pin how Npgsql translates it.
        var day = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var held = await SeedSongAsync(day);
        var alreadyDue = await SeedSongAsync(day.AddDays(1));
        var neverHandedOut = await SeedSongAsync(day.AddDays(2), tagModel: "weak-model");
        var soon = DateTime.UtcNow.AddMinutes(1);
        await using (var context = Database.CreateContext())
        {
            var h = await context.Songs.FindAsync(held.SongId);
            h!.TagAttempts = 6;
            h.NextTagAttemptAt = DateTime.UtcNow.AddHours(1);
            var d = await context.Songs.FindAsync(alreadyDue.SongId);
            d!.TagAttempts = 2;
            d.NextTagAttemptAt = soon;
            await context.SaveChangesAsync();
        }

        var before = DateTime.UtcNow;
        await using (var context = Database.CreateContext())
        {
            var result = await new HoldTagSongsHandler(context, TimeProvider.System)
                .Handle(new HoldTagSongsCommand([held.SongId, alreadyDue.SongId, neverHandedOut.SongId]), CancellationToken.None);
            Assert.True(result.IsOk);
        }

        await using var check = Database.CreateContext();
        var reheld = await check.Songs.FindAsync(held.SongId);
        Assert.Equal(0, reheld!.TagAttempts);
        Assert.InRange(reheld.NextTagAttemptAt!.Value, before, DateTime.UtcNow + GetPendingTagSongsHandler.FirstRetryDelay);
        var due = await check.Songs.FindAsync(alreadyDue.SongId);
        Assert.Equal(0, due!.TagAttempts);
        Assert.Equal(soon, due.NextTagAttemptAt!.Value, TimeSpan.FromMilliseconds(1));
        var untouched = await check.Songs.FindAsync(neverHandedOut.SongId);
        Assert.Null(untouched!.NextTagAttemptAt);
        Assert.Equal(day.AddDays(2), untouched.Version);
    }
}
