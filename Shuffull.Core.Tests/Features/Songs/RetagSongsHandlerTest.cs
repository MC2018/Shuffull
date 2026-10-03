using Nut.Results;
using Shuffull.Core.Features.Songs.RetagSongs;
using Shuffull.Core.Services;

namespace Shuffull.Core.Tests.Features.Songs;

/// <summary>
/// Covers the multi-id targeted retag: per-song outcomes (enriched / skipped-locked / failed), de-dup, the
/// batch cap, and the empty-list short-circuit. Uses a scripted enrichment service (no db needed).
///
/// <para>Also covers the durability contract: every id is promoted out of audition BEFORE any enrichment
/// runs, and that promotion survives the engine failing. See <see cref="Shuffull.Core.Tests.Services.AuditionPromotionServiceTest"/>
/// for the persistence side.</para>
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

    /// <summary>Records what was promoted, and can be scripted to fail (a down/locked database).</summary>
    private sealed class SpyPromotion(Result<int>? scripted = null) : IAuditionPromotionService
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];
        public Task<Result<int>> PromoteAsync(IReadOnlyList<string> songIds, CancellationToken cancellationToken = default!)
        {
            Calls.Add(songIds);
            return Task.FromResult(scripted ?? Result.Ok(songIds.Count));
        }
    }

    /// <summary>Appends "promote:id1,id2" to a shared log so call ORDER across both seams can be asserted.</summary>
    private sealed class SequencedPromotion(List<string> log) : IAuditionPromotionService
    {
        public Task<Result<int>> PromoteAsync(IReadOnlyList<string> songIds, CancellationToken cancellationToken = default!)
        {
            log.Add($"promote:{string.Join(",", songIds)}");
            return Task.FromResult(Result.Ok(songIds.Count));
        }
    }

    /// <summary>Enrichment counterpart of <see cref="SequencedPromotion"/>, writing to the same log.</summary>
    private sealed class SequencedEnrichment(List<string> log, Func<string, Result<SongEnrichmentStatus>> script) : ISongEnrichmentService
    {
        public Task<Result<SongEnrichmentStatus>> EnrichSongAsync(string songId, EnrichmentModel model = EnrichmentModel.Strong, CancellationToken cancellationToken = default!)
        {
            log.Add($"enrich:{songId}");
            return Task.FromResult(script(songId));
        }
    }

    private static Task<Result<RetagSongsResponse>> RunAsync(ISongEnrichmentService enrichment, params string[] ids)
        => RunItemsAsync(enrichment, ids.Select(id => new SongRetagItem(id)).ToArray());

    private static Task<Result<RetagSongsResponse>> RunItemsAsync(ISongEnrichmentService enrichment, params SongRetagItem[] items)
        => new RetagSongsHandler(new SpyPromotion(), enrichment).Handle(new RetagSongsCommand(items), CancellationToken.None);

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

    // ---- Durability contract: the decision outlives the engine -------------------------------------------

    [Fact]
    public async Task PromotesEveryId_BeforeAnyEnrichmentRuns()
    {
        var sequence = new List<string>();
        var promotion = new SequencedPromotion(sequence);
        var enrichment = new SequencedEnrichment(sequence, _ => Result.Ok(SongEnrichmentStatus.Enriched));

        var result = await new RetagSongsHandler(promotion, enrichment)
            .Handle(new RetagSongsCommand([new SongRetagItem("a"), new SongRetagItem("b")]), CancellationToken.None);

        Assert.True(result.IsOk);
        // Promotion must be the FIRST thing that happens, and cover both ids in one write.
        Assert.Equal(["promote:a,b", "enrich:a", "enrich:b"], sequence);
    }

    [Fact]
    public async Task AiDisabled_EveryItemFails_ButThePromotionStillHappened()
    {
        // The exact production case that lost 34 songs: AI__Enabled=false, so the engine errors on every
        // item and the endpoint still returns 200. The keep must survive that.
        var promotion = new SpyPromotion();
        var enrichment = new ScriptedEnrichment(_ => Result.Error<SongEnrichmentStatus>("AI is not enabled; cannot enrich songs."));

        var result = await new RetagSongsHandler(promotion, enrichment)
            .Handle(new RetagSongsCommand([new SongRetagItem("kept", "weak")]), CancellationToken.None);

        Assert.True(result.IsOk);
        Assert.All(result.Get().Results, r => Assert.Equal(Outcomes.Failed, r.Outcome));
        Assert.Equal(["kept"], Assert.Single(promotion.Calls));
    }

    [Fact]
    public async Task UnknownModelTier_StillPromotesThatSong()
    {
        // A typo'd tier is a client bug; it says nothing about whether the user kept the song.
        var promotion = new SpyPromotion();
        var enrichment = new ScriptedEnrichment(_ => Result.Ok(SongEnrichmentStatus.Enriched));

        var result = await new RetagSongsHandler(promotion, enrichment)
            .Handle(new RetagSongsCommand([new SongRetagItem("typo", "weka"), new SongRetagItem("fine", "weak")]), CancellationToken.None);

        Assert.True(result.IsOk);
        Assert.Equal(["typo", "fine"], Assert.Single(promotion.Calls));
        Assert.Equal(["fine"], enrichment.Calls); // still no AI spent on the bad item
    }

    [Fact]
    public async Task PromotionFailure_AbortsBatch_WithoutEnrichingAnything()
    {
        // If we cannot record the decision, enriching anyway would be backwards: the caller must keep its
        // outbox row and retry rather than have the keep quietly evaporate.
        var promotion = new SpyPromotion(Result.Error<int>("db is down"));
        var enrichment = new ScriptedEnrichment(_ => Result.Ok(SongEnrichmentStatus.Enriched));

        var result = await new RetagSongsHandler(promotion, enrichment)
            .Handle(new RetagSongsCommand([new SongRetagItem("a")]), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("db is down", result.GetError().Message);
        Assert.Empty(enrichment.Calls);
    }

    [Fact]
    public async Task DuplicateIds_ArePromotedOnce()
    {
        var promotion = new SpyPromotion();
        var enrichment = new ScriptedEnrichment(_ => Result.Ok(SongEnrichmentStatus.Enriched));

        await new RetagSongsHandler(promotion, enrichment)
            .Handle(new RetagSongsCommand([new SongRetagItem("s1", "weak"), new SongRetagItem("s1", "strong")]), CancellationToken.None);

        Assert.Equal(["s1"], Assert.Single(promotion.Calls));
    }

    [Fact]
    public async Task EmptyList_PromotesNothing()
    {
        var promotion = new SpyPromotion();
        var enrichment = new ScriptedEnrichment(_ => Result.Ok(SongEnrichmentStatus.Enriched));

        var result = await new RetagSongsHandler(promotion, enrichment)
            .Handle(new RetagSongsCommand([]), CancellationToken.None);

        Assert.True(result.IsOk);
        Assert.Empty(promotion.Calls);
    }

    [Fact]
    public async Task OverCap_PromotesNothing()
    {
        // The cap is rejected before any state changes, so an oversized batch can't half-apply.
        var promotion = new SpyPromotion();
        var enrichment = new ScriptedEnrichment(_ => Result.Ok(SongEnrichmentStatus.Enriched));
        var tooMany = Enumerable.Range(0, RetagSongsHandler.MaxBatch + 1).Select(i => new SongRetagItem($"s{i}")).ToArray();

        var result = await new RetagSongsHandler(promotion, enrichment)
            .Handle(new RetagSongsCommand(tooMany), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Empty(promotion.Calls);
        Assert.Empty(enrichment.Calls);
    }
}
