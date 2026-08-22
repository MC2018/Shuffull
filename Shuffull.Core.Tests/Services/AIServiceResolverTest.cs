using Nut.Results;
using Shuffull.Metadata.Configuration;
using Shuffull.Metadata.Models.AI;
using Shuffull.Metadata.Services.AI;

namespace Shuffull.Core.Tests.Services;

/// <summary>
/// The tier -> provider mapping. What matters here is that tier and vendor stay separate axes: a tier picks a
/// provider, and the MODEL then comes from that provider's own names — so pointing the weak tier at a second
/// vendor cannot send it a model id belonging to the first one.
/// </summary>
public class AIServiceResolverTest
{
    private sealed class StubAiService : IAIService
    {
        public Task<Result<GenerateMainGenresResponse>> GenerateMainGenresAsync(GenerateMainGenresRequest request, CancellationToken cancellationToken = default!)
            => throw new NotSupportedException();
        public Task<Result<GenerateSubGenresResponse>> GenerateSubGenresAsync(GenerateSubGenresRequest request, CancellationToken cancellationToken = default!)
            => throw new NotSupportedException();
        public Task<Result<GenerateOtherSongDetailsResponse>> GenerateOtherSongDetailsAsync(GenerateOtherSongDetailsRequest request, CancellationToken cancellationToken = default!)
            => throw new NotSupportedException();
    }

    private static AiProvider Provider(string name, string strong, string weak = "") =>
        new(name, new StubAiService(), new OpenAIConfiguration
        {
            ApiKey = "test-key",
            StrongModelName = strong,
            WeakModelName = weak,
        });

    [Fact]
    public void DefaultTiers_SendBothTiersToOpenAI()
    {
        var openAi = Provider("OpenAI", strong: "gpt-5.5", weak: "gpt-5-mini");
        var resolver = new AIServiceResolver([openAi], new AiTierConfiguration());

        Assert.Equal("gpt-5-mini", resolver.Resolve(AiTier.Weak).Model);
        Assert.Equal("gpt-5.5", resolver.Resolve(AiTier.Strong).Model);
        Assert.Same(openAi.Service, resolver.Resolve(AiTier.Strong).Service);
    }

    [Fact]
    public void WeakTier_CanBeServedByADifferentVendor_UsingThatVendorsModel()
    {
        var openAi = Provider("OpenAI", strong: "gpt-5.5", weak: "gpt-5-mini");
        var meta = Provider("Meta", strong: "muse-spark-1.2-contributor", weak: "muse-spark-1.2-contributor");
        var resolver = new AIServiceResolver([openAi, meta],
            new AiTierConfiguration { Weak = "Meta", Strong = "OpenAI" });

        var weak = resolver.Resolve(AiTier.Weak);
        var strong = resolver.Resolve(AiTier.Strong);

        // The weak model is Meta's, NOT OpenAI's — the bug this shape exists to prevent.
        Assert.Same(meta.Service, weak.Service);
        Assert.Equal("muse-spark-1.2-contributor", weak.Model);
        Assert.Same(openAi.Service, strong.Service);
        Assert.Equal("gpt-5.5", strong.Model);
    }

    [Fact]
    public void ProviderNamesAreCaseInsensitive()
    {
        var resolver = new AIServiceResolver(
            [Provider("OpenAI", strong: "gpt-5.5", weak: "gpt-5-mini")],
            new AiTierConfiguration { Weak = "openai", Strong = "OPENAI" });

        Assert.Equal("gpt-5-mini", resolver.Resolve(AiTier.Weak).Model);
    }

    [Fact]
    public void AnUnconfiguredProviderNameFailsAtConstruction_NotAtFirstCall()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new AIServiceResolver(
            [Provider("OpenAI", strong: "gpt-5.5", weak: "gpt-5-mini")],
            new AiTierConfiguration { Weak = "Metaa", Strong = "OpenAI" }));

        Assert.Contains("Metaa", ex.Message);
    }

    [Fact]
    public void AWeakTierWithNoWeakModel_FallsBackToTheProvidersStrongModel()
    {
        // ResolvedWeakModelName already falls back to the strong model, so an unset weak name is "use the
        // strong one" rather than an error — weak-model behaviour stays strictly opt-in.
        var resolver = new AIServiceResolver(
            [Provider("OpenAI", strong: "gpt-5.5")],
            new AiTierConfiguration());

        Assert.Equal("gpt-5.5", resolver.Resolve(AiTier.Weak).Model);
    }

    [Fact]
    public void AProviderWithNoModelsAtAllIsRejectedWhenResolved()
    {
        var resolver = new AIServiceResolver(
            [Provider("OpenAI", strong: "")],
            new AiTierConfiguration());

        var ex = Assert.Throws<InvalidOperationException>(() => resolver.Resolve(AiTier.Strong));
        Assert.Contains("no model configured", ex.Message);
    }
}
