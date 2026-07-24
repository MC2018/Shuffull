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
    /// <summary>Returns a per-id scripted result; records call order + the model each call ran.</summary>
    private sealed class ScriptedEnrichment(Func<string, Result<SongEnrichmentStatus>> script) : ISongEnrichmentService
    {
        public List<string> Calls { get; } = [];
        public List<EnrichmentModel> Models { get; } = [];
        public Task<Result<SongEnrichmentStatus>> EnrichSongAsync(string songId, EnrichmentModel model = EnrichmentModel.Strong, CancellationToken cancellationToken = default!)
        {
            Calls.Add(songId);
            Models.Add(model);
            return Task.FromResult(script(songId));
        }
    }

    private static Task<Result<RetagSongsResponse>> RunAsync(ISongEnrichmentService enrichment, params string[] ids)
        => RunItemsAsync(enrichment, ids.Select(id => new SongRetagItem(id)).ToArray());

    private static Task<Result<RetagSongsResponse>> RunItemsAsync(ISongEnrichmentService enrichment, params SongRetagItem[] items)
        => new RetagSongsHandler(enrichment).Handle(new RetagSongsCommand(items), CancellationToken.None);

    [Theory]
    [InlineData(null, EnrichmentModel.Strong)]
    [InlineData("strong", EnrichmentModel.Strong)]
    [InlineData("Weak", EnrichmentModel.Weak)] // case-insensitive
    [InlineData("weak", EnrichmentModel.Weak)]
    public async Task ModelTier_IsPassedThrough_PerItem(string? wireModel, EnrichmentModel expected)
    {
        var enrichment = new ScriptedEnrichment(_ => Result.Ok(SongEnrichmentStatus.Enriched));

        var result = await RunItemsAsync(enrichment, new SongRetagItem("s1", wireModel), new SongRetagItem("s2", wireModel));

        Assert.True(result.IsOk);
        Assert.All(enrichment.Models, m => Assert.Equal(expected, m));
    }

    [Fact]
    public async Task MixedTiers_FlushInOneBatch_EachItemRunsItsOwnModel()
    {
        var enrichment = new ScriptedEnrichment(_ => Result.Ok(SongEnrichmentStatus.Enriched));

        var result = await RunItemsAsync(enrichment,
            new SongRetagItem("kept", "weak"),
            new SongRetagItem("liked", "strong"),
            new SongRetagItem("defaulted"));

        Assert.True(result.IsOk);
        Assert.Equal(["kept", "liked", "defaulted"], enrichment.Calls);
        Assert.Equal([EnrichmentModel.Weak, EnrichmentModel.Strong, EnrichmentModel.Strong], enrichment.Models);
    }

    [Theory]
    [InlineData("weak", "strong")] // Keep then Like
    [InlineData("strong", "weak")] // Like then Keep
    public async Task DuplicateSongId_CollapsesStrongerWins(string first, string second)
    {
        var enrichment = new ScriptedEnrichment(_ => Result.Ok(SongEnrichmentStatus.Enriched));

        var result = await RunItemsAsync(enrichment, new SongRetagItem("s1", first), new SongRetagItem("s1", second));

        Assert.True(result.IsOk);
        Assert.Equal(["s1"], enrichment.Calls); // one AI run, not two
        Assert.Equal([EnrichmentModel.Strong], enrichment.Models);
    }

    [Fact]
    public async Task UnknownModelTier_FailsOnlyThatItem_OthersProceed()
    {
        var enrichment = new ScriptedEnrichment(_ => Result.Ok(SongEnrichmentStatus.Enriched));

        var result = await RunItemsAsync(enrichment, new SongRetagItem("typo", "weka"), new SongRetagItem("fine", "weak"));

        Assert.True(result.IsOk);
        Assert.Equal(["fine"], enrichment.Calls); // the typo'd item spends no AI money
        Assert.Collection(result.Get().Results,
            r => { Assert.Equal("typo", r.SongId); Assert.Equal(Outcomes.Failed, r.Outcome); Assert.Contains("Unknown model tier", r.Error); },
            r => { Assert.Equal("fine", r.SongId); Assert.Equal(Outcomes.Enriched, r.Outcome); });
    }

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
