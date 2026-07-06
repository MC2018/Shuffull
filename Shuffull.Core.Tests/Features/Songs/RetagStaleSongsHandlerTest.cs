using Microsoft.Extensions.Configuration;
using Nut.Results;
using Shuffull.Core.Features.Songs.RetagStaleSongs;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Services;
using Shuffull.Core.Tests.Infrastructure;
using Shuffull.Core.Tools;
using Shuffull.Shared.Tools;

namespace Shuffull.Core.Tests.Features.Songs;

/// <summary>
/// Covers the batch re-tag selection: only weaker/null-TagModel unlocked songs are picked (oldest-version
/// first), bounded by the limit, with an accurate "remaining" count - plus the fail-safe when the current
/// strong model isn't registered.
/// </summary>
public class RetagStaleSongsHandlerTest : IDisposable
{
    private readonly DatabaseFixture _database;

    public RetagStaleSongsHandlerTest() => _database = new DatabaseFixture();

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Records the ids it was asked to enrich; "enriches" without touching the db.</summary>
    private sealed class RecordingEnrichment : ISongEnrichmentService
    {
        public List<string> Enriched { get; } = [];
        public Task<Result<SongEnrichmentStatus>> EnrichSongAsync(string songId, CancellationToken cancellationToken = default!)
        {
            Enriched.Add(songId);
            return Task.FromResult(Result.Ok(SongEnrichmentStatus.Enriched));
        }
    }

    private static ModelStrengths Strengths() => new(new Dictionary<string, int>
    {
        ["weak"] = 10,
        ["strong"] = 30,
    });

    private static IConfiguration Config(string? strongModel) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AI:OpenAI:StrongModelName"] = strongModel })
            .Build();

    private async Task<Song> SeedSongAsync(string? tagModel, bool locked, DateTime version, bool exploratory = false)
    {
        var song = new Song
        {
            SongId = IdGenerator.Generate(),
            Name = $"song-{tagModel ?? "null"}-{version.Ticks}",
            FileExtension = ".mp3",
            FileHash = IdGenerator.Generate(),
            TagModel = tagModel,
            MetadataLocked = locked,
            Exploratory = exploratory,
            Version = version,
        };
        _database.Context.Songs.Add(song);
        await _database.Context.SaveChangesAsync();
        return song;
    }

    private Task<Result<RetagStaleSongsResponse>> RunAsync(RecordingEnrichment enrichment, string? strongModel, int limit)
        => new RetagStaleSongsHandler(_database.Context, Strengths(), Config(strongModel), enrichment)
            .Handle(new RetagStaleSongsCommand(limit), CancellationToken.None);

    [Fact]
    public async Task SelectsWeakAndNull_ExcludesStrongAndLocked_OldestFirst_RespectsLimit()
    {
        var baseTime = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var strong = await SeedSongAsync("strong", locked: false, baseTime);            // not stale
        var lockedWeak = await SeedSongAsync("weak", locked: true, baseTime);           // stale-by-model but locked
        var weakOldest = await SeedSongAsync("weak", locked: false, baseTime.AddDays(1));
        var nullNewer = await SeedSongAsync(null, locked: false, baseTime.AddDays(2));

        var enrichment = new RecordingEnrichment();
        var result = await RunAsync(enrichment, "strong", limit: 1);

        Assert.True(result.IsOk);
        var response = result.Get();
        Assert.Equal(1, response.Enriched);
        Assert.Equal(0, response.Failed);
        Assert.Equal(1, response.Remaining); // 2 stale (weakOldest + nullNewer) - 1 enriched
        Assert.Equal("strong", response.StrongModel);

        // Oldest stale first, and neither the strong nor the locked song was touched.
        Assert.Equal([weakOldest.SongId], enrichment.Enriched);
        Assert.DoesNotContain(strong.SongId, enrichment.Enriched);
        Assert.DoesNotContain(lockedWeak.SongId, enrichment.Enriched);
    }

    [Fact]
    public async Task DrainsAllStale_WhenLimitHighEnough()
    {
        var baseTime = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedSongAsync("weak", locked: false, baseTime);
        await SeedSongAsync(null, locked: false, baseTime.AddDays(1));

        var enrichment = new RecordingEnrichment();
        var result = await RunAsync(enrichment, "strong", limit: 100);

        var response = result.Get();
        Assert.Equal(2, response.Enriched);
        Assert.Equal(0, response.Remaining);
    }

    [Fact]
    public async Task ExploratorySongs_AreExcluded()
    {
        var baseTime = new DateTime(2023, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var exploratory = await SeedSongAsync("weak", locked: false, baseTime, exploratory: true); // weak but un-vetted
        var normalWeak = await SeedSongAsync("weak", locked: false, baseTime.AddDays(1));

        var enrichment = new RecordingEnrichment();
        var result = await RunAsync(enrichment, "strong", limit: 100);

        var response = result.Get();
        Assert.Equal(1, response.Enriched);          // only the normal weak song
        Assert.Equal(0, response.Remaining);
        Assert.Equal([normalWeak.SongId], enrichment.Enriched);
        Assert.DoesNotContain(exploratory.SongId, enrichment.Enriched);
    }

    [Fact]
    public async Task UnregisteredStrongModel_IsFailSafe_NothingStale()
    {
        var baseTime = new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedSongAsync(null, locked: false, baseTime);
        await SeedSongAsync("weak", locked: false, baseTime.AddDays(1));

        var enrichment = new RecordingEnrichment();
        var result = await RunAsync(enrichment, strongModel: "gpt-6-unregistered", limit: 100);

        var response = result.Get();
        Assert.Equal(0, response.Enriched);
        Assert.Equal(0, response.Remaining);
        Assert.Empty(enrichment.Enriched);
    }
}
