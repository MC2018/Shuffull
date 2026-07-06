using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Metadata.Models.AI;
using Shuffull.Metadata.Services.AI;
using Shuffull.Api.Services;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Services;
using Shuffull.Core.Tests.Infrastructure;
using Shuffull.Shared.Tools;

namespace Shuffull.Core.Tests.Services;

/// <summary>
/// Exercises SongEnrichmentService.EnrichSongCoreAsync (the on-demand re-tag primitive) against the in-memory
/// SQLite fixture with a scripted engine: tag-join rewrite + provenance stamping, the MetadataLocked guard,
/// the stored-year era override, and the BPM plausibility rule.
/// </summary>
public class SongEnrichmentServiceTest : IDisposable
{
    private readonly DatabaseFixture _database;

    public SongEnrichmentServiceTest()
    {
        _database = new DatabaseFixture();
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Scripted engine: returns fixed responses and records the requests it saw.</summary>
    private sealed class FakeAiService : IAIService
    {
        public List<string> MainGenres { get; init; } = ["Electronic"];
        public List<string> SubGenres { get; init; } = ["House"];
        public GenerateOtherSongDetailsResponse Other { get; init; } =
            new("2020s", ["Instrumental"], ["Energetic"], 7, [], TrueBpm: 175, BpmRecognized: true);

        public GenerateMainGenresRequest? SeenMainRequest { get; private set; }
        public GenerateSubGenresRequest? SeenSubRequest { get; private set; }
        public GenerateOtherSongDetailsRequest? SeenOtherRequest { get; private set; }

        public Task<Result<GenerateMainGenresResponse>> GenerateMainGenresAsync(GenerateMainGenresRequest request, CancellationToken cancellationToken = default!)
        {
            SeenMainRequest = request;
            return Task.FromResult(Result.Ok(new GenerateMainGenresResponse(MainGenres)));
        }

        public Task<Result<GenerateSubGenresResponse>> GenerateSubGenresAsync(GenerateSubGenresRequest request, CancellationToken cancellationToken = default!)
        {
            SeenSubRequest = request;
            return Task.FromResult(Result.Ok(new GenerateSubGenresResponse(SubGenres)));
        }

        public Task<Result<GenerateOtherSongDetailsResponse>> GenerateOtherSongDetailsAsync(GenerateOtherSongDetailsRequest request, CancellationToken cancellationToken = default!)
        {
            SeenOtherRequest = request;
            return Task.FromResult(Result.Ok(Other));
        }
    }

    /// <summary>Seeds the genre vocabulary: Electronic (main, subs House/Techno) and Rock (main, sub Rock).</summary>
    private async Task SeedGenresAsync()
    {
        var electronic = new Genre { TagId = IdGenerator.Generate(), Name = "Electronic" };
        var house = new Genre { TagId = IdGenerator.Generate(), Name = "House" };
        var techno = new Genre { TagId = IdGenerator.Generate(), Name = "Techno" };
        var rock = new Genre { TagId = IdGenerator.Generate(), Name = "Rock" };
        _database.Context.AddRange(electronic, house, techno, rock);
        _database.Context.AddRange(
            new GenreRelation { GenreRelationId = IdGenerator.Generate(), MainGenre = electronic, SubGenre = house },
            new GenreRelation { GenreRelationId = IdGenerator.Generate(), MainGenre = electronic, SubGenre = techno },
            new GenreRelation { GenreRelationId = IdGenerator.Generate(), MainGenre = rock, SubGenre = rock });
        await _database.Context.SaveChangesAsync();
    }

    private async Task<Song> SeedSongAsync(bool locked = false, int? measuredBpm = 128, int? originalReleaseYear = null, bool exploratory = false)
    {
        var song = new Song
        {
            SongId = IdGenerator.Generate(),
            Name = "Test Song",
            FileExtension = ".mp3",
            FileHash = IdGenerator.Generate(),
            MetadataLocked = locked,
            MeasuredBpm = measuredBpm,
            Bpm = measuredBpm,
            OriginalReleaseYear = originalReleaseYear,
            Exploratory = exploratory,
            TagModel = "old-weak-model",
            Version = DateTime.UtcNow.AddDays(-30),
        };
        var artist = new Artist { ArtistId = IdGenerator.Generate(), Name = "Test Artist" };
        _database.Context.Add(song);
        _database.Context.Add(artist);
        _database.Context.Add(new SongArtist { SongArtistId = IdGenerator.Generate(), SongId = song.SongId, ArtistId = artist.ArtistId });

        // A stale machine tag from the previous model (a distinct name, not in the vocab); enrichment replaces it.
        var oldTag = new Mood { TagId = IdGenerator.Generate(), Name = "StaleOldMood" };
        _database.Context.Add(oldTag);
        _database.Context.Add(new SongTag { SongTagId = IdGenerator.Generate(), SongId = song.SongId, TagId = oldTag.TagId });
        await _database.Context.SaveChangesAsync();
        return song;
    }

    private Task<Result<SongEnrichmentStatus>> EnrichAsync(FakeAiService ai, string songId) =>
        SongEnrichmentService.EnrichSongCoreAsync(
            _database.Context, ai, "strong-model-x",
            candidateMoods: ["Energetic", "Chill"], candidateThemes: ["Anime"],
            songId, CancellationToken.None);

    [Fact]
    public async Task Enrich_RewritesTagsAndStampsProvenance()
    {
        await SeedGenresAsync();
        var song = await SeedSongAsync();
        var ai = new FakeAiService();

        var result = await EnrichAsync(ai, song.SongId);

        Assert.True(result.IsOk);
        Assert.Equal(SongEnrichmentStatus.Enriched, result.Get());

        var tagNames = await _database.Context.SongTags
            .Where(st => st.SongId == song.SongId)
            .Join(_database.Context.Tags, st => st.TagId, t => t.TagId, (st, t) => t.Name)
            .ToListAsync();
        Assert.Contains("Electronic", tagNames);
        Assert.Contains("House", tagNames);
        Assert.Contains("Instrumental", tagNames);
        Assert.Contains("2020s", tagNames);
        Assert.Contains("Energetic", tagNames);
        Assert.DoesNotContain("StaleOldMood", tagNames); // the stale machine tag is gone

        var updated = await _database.Context.Songs.AsNoTracking().SingleAsync(s => s.SongId == song.SongId);
        Assert.Equal("strong-model-x", updated.TagModel);
        Assert.Equal(7, updated.Energy);
        Assert.Equal(175, updated.Bpm); // plausible TrueBpm wins over the measured 128
        Assert.True(updated.Version > DateTime.UtcNow.AddDays(-1));

        // The engine saw the stored measured tempo and the narrowed sub-candidates.
        Assert.Equal(128, ai.SeenOtherRequest!.MeasuredBpm);
        Assert.Equal(["House", "Techno"], ai.SeenSubRequest!.SubGenres.Order().ToList());
        Assert.Equal(["Energetic", "Chill"], ai.SeenOtherRequest!.CandidateMoods);
    }

    [Fact]
    public async Task Enrich_SkipsMetadataLockedUntouched()
    {
        await SeedGenresAsync();
        var song = await SeedSongAsync(locked: true);

        var result = await EnrichAsync(new FakeAiService(), song.SongId);

        Assert.True(result.IsOk);
        Assert.Equal(SongEnrichmentStatus.SkippedMetadataLocked, result.Get());
        var untouched = await _database.Context.Songs.AsNoTracking().SingleAsync(s => s.SongId == song.SongId);
        Assert.Equal("old-weak-model", untouched.TagModel);
    }

    [Fact]
    public async Task Enrich_StoredYearOverridesModelEra()
    {
        await SeedGenresAsync();
        var song = await SeedSongAsync(originalReleaseYear: 1997);
        var ai = new FakeAiService(); // model claims 2020s

        var result = await EnrichAsync(ai, song.SongId);

        Assert.True(result.IsOk);
        var tagNames = await _database.Context.SongTags
            .Where(st => st.SongId == song.SongId)
            .Join(_database.Context.Tags, st => st.TagId, t => t.TagId, (st, t) => t.Name)
            .ToListAsync();
        Assert.Contains("1990s", tagNames);
        Assert.DoesNotContain("2020s", tagNames);
    }

    [Fact]
    public async Task Enrich_ImplausibleTrueBpm_KeepsMeasured()
    {
        await SeedGenresAsync();
        var song = await SeedSongAsync(measuredBpm: 128);
        var ai = new FakeAiService
        {
            Other = new GenerateOtherSongDetailsResponse("2010s", ["Instrumental"], [], 5, [], TrueBpm: 20, BpmRecognized: false),
        };

        var result = await EnrichAsync(ai, song.SongId);

        Assert.True(result.IsOk);
        var updated = await _database.Context.Songs.AsNoTracking().SingleAsync(s => s.SongId == song.SongId);
        Assert.Equal(128, updated.Bpm);
    }

    [Fact]
    public async Task Enrich_MissingSong_Errors()
    {
        var result = await EnrichAsync(new FakeAiService(), "no-such-song");
        Assert.True(result.IsError);
    }

    [Fact]
    public async Task Enrich_PromotesExploratorySong()
    {
        await SeedGenresAsync();
        var song = await SeedSongAsync(exploratory: true);

        var result = await EnrichAsync(new FakeAiService(), song.SongId);

        Assert.True(result.IsOk);
        var updated = await _database.Context.Songs.AsNoTracking().SingleAsync(s => s.SongId == song.SongId);
        Assert.False(updated.Exploratory); // keeping/enriching promotes it out of audition state
        Assert.Equal("strong-model-x", updated.TagModel);
    }

    [Fact]
    public void LyricsContext_PrefersPlain_StripsLrc_AndFlagsInstrumental()
    {
        Assert.Equal("The track is instrumental (no lyrics).",
            SongEnrichmentService.BuildLyricsContext(new Song { LyricsInstrumental = true }));

        var fromPlain = SongEnrichmentService.BuildLyricsContext(new Song { PlainLyrics = "line one\nline two" });
        Assert.Equal("Lyrics:\nline one\nline two", fromPlain);

        var fromSynced = SongEnrichmentService.BuildLyricsContext(new Song { SyncedLyrics = "[00:01.00]hello\n[00:02.00]world" });
        Assert.Equal("Lyrics:\nhello\nworld", fromSynced);

        Assert.Null(SongEnrichmentService.BuildLyricsContext(new Song()));
    }
}
