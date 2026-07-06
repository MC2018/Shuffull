using Nut.Results;
using Shuffull.Core.Features.Songs.RetagSongs;
using Shuffull.Core.Services;

namespace Shuffull.Core.Tests.Features.Songs;

/// <summary>
/// Covers the multi-id targeted retag: per-song outcomes (enriched / skipped-locked / failed), de-dup, the
/// batch cap, and the empty-list short-circuit. Uses a scripted enrichment service (no db needed).
/// </summary>
public class RetagSongsHandlerTest
{
    /// <summary>Returns a per-id scripted result; records call order.</summary>
    private sealed class ScriptedEnrichment(Func<string, Result<SongEnrichmentStatus>> script) : ISongEnrichmentService
    {
        public List<string> Calls { get; } = [];
        public Task<Result<SongEnrichmentStatus>> EnrichSongAsync(string songId, CancellationToken cancellationToken = default!)
        {
            Calls.Add(songId);
            return Task.FromResult(script(songId));
        }
    }

    private static Task<Result<RetagSongsResponse>> RunAsync(ISongEnrichmentService enrichment, params string[] ids)
        => new RetagSongsHandler(enrichment).Handle(new RetagSongsCommand(ids), CancellationToken.None);

    [Fact]
    public async Task MapsPerSongOutcomes()
    {
        var enrichment = new ScriptedEnrichment(id => id switch
        {
            "ok" => Result.Ok(SongEnrichmentStatus.Enriched),
            "locked" => Result.Ok(SongEnrichmentStatus.SkippedMetadataLocked),
            _ => Result.Error<SongEnrichmentStatus>("boom"),
        });

        var response = (await RunAsync(enrichment, "ok", "locked", "bad")).Get();

        Assert.Collection(response.Results,
            r => { Assert.Equal("ok", r.SongId); Assert.Equal(Outcomes.Enriched, r.Outcome); Assert.Null(r.Error); },
            r => { Assert.Equal("locked", r.SongId); Assert.Equal(Outcomes.Skipped, r.Outcome); },
            r => { Assert.Equal("bad", r.SongId); Assert.Equal(Outcomes.Failed, r.Outcome); Assert.Equal("boom", r.Error); });
    }

    [Fact]
    public async Task DeDupesIds_AndDropsBlank()
    {
        var enrichment = new ScriptedEnrichment(_ => Result.Ok(SongEnrichmentStatus.Enriched));

        var response = (await RunAsync(enrichment, "a", "a", " ", "b", "")).Get();

        Assert.Equal(["a", "b"], enrichment.Calls);
        Assert.Equal(2, response.Results.Count);
    }

    [Fact]
    public async Task EmptyList_ShortCircuits()
    {
        var enrichment = new ScriptedEnrichment(_ => Result.Ok(SongEnrichmentStatus.Enriched));

        var response = (await RunAsync(enrichment)).Get();

        Assert.Empty(response.Results);
        Assert.Empty(enrichment.Calls);
    }

    [Fact]
    public async Task OverCap_Errors_WithoutEnrichingAnything()
    {
        var enrichment = new ScriptedEnrichment(_ => Result.Ok(SongEnrichmentStatus.Enriched));
        var tooMany = Enumerable.Range(0, RetagSongsHandler.MaxBatch + 1).Select(i => $"s{i}").ToArray();

        var result = await RunAsync(enrichment, tooMany);

        Assert.True(result.IsError);
        Assert.Empty(enrichment.Calls); // rejected before any work
    }
}
