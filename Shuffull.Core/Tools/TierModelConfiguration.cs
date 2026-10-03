using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shuffull.Metadata.Tools;

namespace Shuffull.Core.Tools;

/// <summary>
/// The model each AI tier resolves to, read straight from configuration. Every reader that judges tags against
/// "the current weak/strong model" goes through here, so the work queue (pending-tags), the sync payload's
/// TagsStale flag and the curator re-tag sweep can never disagree about what strong means.
///
/// Deliberately NOT IAIServiceResolver: that is only registered when the site's own AI is enabled, and these
/// decisions must work with it off (all AI spend is the funnel's). It mirrors the resolver's chain instead:
/// <c>AI:Tiers:{tier}</c> names a provider section, the strong model falls back to the legacy <c>ModelName</c>,
/// and a missing weak model falls back to that provider's strong one.
///
/// Blank counts as missing at every step. Compose renders an unset variable as <c>KEY=</c>, which binds as an
/// empty string rather than null — so a <c>??</c> chain stops at "" and the tier quietly resolves to a model
/// with strength 0. That is how prod's strong tier went dark (Shuffull#36).
/// </summary>
public static class TierModelConfiguration
{
    public const string Weak = "Weak";
    public const string Strong = "Strong";

    private const string DefaultProvider = "OpenAI";

    /// <summary>The provider section serving <paramref name="tier"/> ("OpenAI" when unset).</summary>
    public static string ResolveProvider(IConfiguration configuration, string tier) =>
        NonBlank(configuration[$"AI:Tiers:{tier}"]) ?? DefaultProvider;

    /// <summary>The model <paramref name="tier"/> runs, or null when nothing is configured for it.</summary>
    public static string? ResolveModel(IConfiguration configuration, string tier)
    {
        var provider = ResolveProvider(configuration, tier);
        var strong = NonBlank(configuration[$"AI:{provider}:StrongModelName"])
                     ?? NonBlank(configuration[$"AI:{provider}:ModelName"]);

        return tier == Weak
            ? NonBlank(configuration[$"AI:{provider}:WeakModelName"]) ?? strong
            : strong;
    }

    /// <summary>
    /// Warns once per tier whose model scores 0 in <paramref name="strengths"/>. Every consumer of the ladder
    /// fails SAFE on a 0 — nothing reads stale, nothing is offered for upgrade — which is the right failure but
    /// a silent one: it looks like "likes stopped upgrading tags", months later. This is the only place it shows.
    /// </summary>
    /// <returns>The tiers that were warned about, for tests.</returns>
    public static IReadOnlyList<string> WarnOnUnregisteredModels(IConfiguration configuration, ModelStrengths strengths, ILogger logger)
    {
        var warned = new List<string>();
        foreach (var tier in new[] { Weak, Strong })
        {
            var model = ResolveModel(configuration, tier);
            if (strengths.Knows(model))
            {
                continue;
            }

            var provider = ResolveProvider(configuration, tier);
            if (model is null)
            {
                logger.LogWarning(
                    "AI {Tier} tier has no model: every model name under AI:{Provider} is unset or blank, so {Consequence}. "
                    + "Set the model name in the env file and redeploy.",
                    tier, provider, Consequence(tier));
            }
            else
            {
                logger.LogWarning(
                    "AI {Tier} tier model '{Model}' (provider {Provider}) is not in AI:ModelStrengths, so it scores 0 and {Consequence}. "
                    + "Register it in appsettings.json and in the funnel's matching map.",
                    tier, model, provider, Consequence(tier));
            }

            warned.Add(tier);
        }

        return warned;
    }

    private static string Consequence(string tier) => tier == Strong
        ? "a like will never queue a strong re-tag and no song is ever flagged TagsStale"
        : "a song tagged by any registered model counts as done for the weak tier";

    private static string? NonBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
