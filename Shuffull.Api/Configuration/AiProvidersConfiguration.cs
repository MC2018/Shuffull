using Shuffull.Metadata.Configuration;
using Shuffull.Metadata.Services.AI;

namespace Shuffull.Api.Configuration;

/// <summary>
/// Registers the named AI providers and the tier -> provider resolver.
/// </summary>
/// <remarks>
/// Providers are sibling sections under <c>AI:</c> ("OpenAI", "Meta", …), each binding to an
/// <see cref="OpenAIConfiguration"/>; <c>AI:Tiers</c> says which one serves the weak and strong tiers. Any
/// vendor speaking the OpenAI chat/completions shape needs only a section and a <c>BaseUrl</c> — Meta's Model
/// API does, which is why there is no MetaAIService: it would be a class whose only job is holding a URL.
///
/// Additive on purpose. <c>AddOpenAIService</c> still registers the plain <see cref="IAIService"/> and
/// <see cref="OpenAIConfiguration"/> that SongEnrichmentService and SongImportService resolve today, so an
/// install that never mentions tiers keeps its current single-provider behaviour.
/// </remarks>
public static class AiProvidersConfigurationExtensions
{
    public static IServiceCollection AddAiProviders(this IServiceCollection collection, ConfigurationManager configurationManager)
    {
        var tiers = configurationManager
            .GetSection(AiTierConfiguration.AiTierConfigurationSection)
            .Get<AiTierConfiguration>() ?? new AiTierConfiguration();

        // "OpenAI" is always included even when no tier names it, because the rest of the app still reads
        // AI:OpenAI directly (TagModel stamping, the import tagger) and a resolver that disagreed with those
        // call sites about which models exist would be worse than one extra binding.
        var names = new[] { "OpenAI", tiers.Weak, tiers.Strong }
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var providers = new List<AiProvider>();

        foreach (var name in names)
        {
            var config = configurationManager
                .GetSection($"{AIConfiguration.AIConfigurationSection}:{name}")
                .Get<OpenAIConfiguration>();

            var servesATier = name.Equals(tiers.Weak, StringComparison.OrdinalIgnoreCase)
                || name.Equals(tiers.Strong, StringComparison.OrdinalIgnoreCase);

            if (config == null || string.IsNullOrWhiteSpace(config.ApiKey))
            {
                // A provider that is merely present but unused may sit there blank — that is what lets the
                // Meta section ship with an empty key until someone fills it in. One a tier actually points
                // at must work, and saying so at startup beats discovering it mid-enrichment.
                if (servesATier)
                {
                    throw new InvalidOperationException(
                        $"AI provider '{name}' serves a tier but has no ApiKey. Set " +
                        $"{AIConfiguration.AIConfigurationSection}:{name}:ApiKey " +
                        $"(env {AIConfiguration.AIConfigurationSection}__{name}__ApiKey).");
                }

                continue;
            }

            // OpenAIService holds nothing but its config — the chat client is built per call — so the second
            // instance for "OpenAI" costs nothing and keeps this registration independent of AddOpenAIService.
            providers.Add(new AiProvider(name, new OpenAIService(config), config));
        }

        collection.AddSingleton(tiers);
        collection.AddSingleton<IAIServiceResolver>(new AIServiceResolver(providers, tiers));

        return collection;
    }
}
