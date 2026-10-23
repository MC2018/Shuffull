using Microsoft.Extensions.Configuration;
using Nut.Results;
using Shuffull.Core.Features.Songs.GetPendingTagSongs;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Tests.Infrastructure;
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
    });

    private static readonly IConfiguration Config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["AI:OpenAI:WeakModelName"] = "weak-model",
        ["AI:OpenAI:StrongModelName"] = "strong-model",
    }).Build();

    private async Task<Song> SeedSongAsync(DateTime version, string? tagModel = null, string? likedBy = null)
    {
        await using var context = Database.CreateContext();
        var song = new Song
        {
            SongId = Guid.NewGuid().ToString(),
            Name = $"song-{Guid.NewGuid():N}",
            FileExtension = ".mp3",
            FileHash = Guid.NewGuid().ToString(),
            TagModel = tagModel,
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
        return (await new GetPendingTagSongsHandler(context, Strengths, Config)
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
}
