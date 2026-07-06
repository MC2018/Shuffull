namespace Shuffull.Core.Tools;

/// <summary>
/// Relative-strength registry for AI models (provider-neutral): bigger integer = stronger model. Drives the
/// "should this song be re-tagged?" decision - a song is stale when the model that produced its tags
/// (<c>Song.TagModel</c>) is weaker than the current strong model.
///
/// Fail-safe by construction: an unknown or null model resolves to strength 0, and staleness is a strict
/// less-than against the CURRENT strong model's strength - so if the current model was never registered
/// (strength 0), nothing is ever stale. Forgetting to register a newly-adopted model therefore results in a
/// no-op (callers should log a warning via <see cref="Knows"/>), never a surprise whole-library re-tag.
/// Songs with a null TagModel (the pre-provenance library, or self-tagged rows that predate stamping) have
/// strength 0 and are stale whenever a registered strong model is in play - which is exactly the upgrade
/// scenario the registry exists for.
/// </summary>
public sealed class ModelStrengths
{
    private readonly Dictionary<string, int> _strengths;

    public ModelStrengths(IReadOnlyDictionary<string, int>? strengths = null)
    {
        _strengths = strengths is null
            ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, int>(strengths, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The registered strength for <paramref name="model"/>, or 0 when null/unregistered.</summary>
    public int GetStrength(string? model) =>
        model is not null && _strengths.TryGetValue(model, out var strength) ? strength : 0;

    /// <summary>True when <paramref name="model"/> is registered (lets callers warn on unregistered models).</summary>
    public bool Knows(string? model) => model is not null && _strengths.ContainsKey(model);

    /// <summary>
    /// True when the tags produced by <paramref name="tagModel"/> should be regenerated with
    /// <paramref name="currentStrongModel"/>. Strict less-than: equal strength (including both-unknown) is
    /// never stale, so an unregistered current model can't trigger a mass re-tag.
    /// </summary>
    public bool IsStale(string? tagModel, string? currentStrongModel) =>
        GetStrength(tagModel) < GetStrength(currentStrongModel);

    /// <summary>
    /// The registered model names whose strength is at least that of <paramref name="currentStrongModel"/> -
    /// i.e. the models a song's TagModel could carry and NOT be stale. A song is stale exactly when its TagModel
    /// is not in this set (including null / unregistered), which lets callers push the selection into SQL as a
    /// <c>TagModel IS NULL OR TagModel NOT IN (...)</c> filter instead of loading every row.
    ///
    /// IMPORTANT: only meaningful when the current strong model has a positive strength. When it is 0
    /// (unregistered / null), this returns every registered model, so callers MUST first short-circuit on
    /// <see cref="GetStrength"/> == 0 to preserve the fail-safe (nothing is stale) - otherwise a null TagModel
    /// would wrongly fall outside the set and look stale.
    /// </summary>
    public IReadOnlyList<string> ModelsAtLeastAsStrongAs(string? currentStrongModel)
    {
        var threshold = GetStrength(currentStrongModel);
        return _strengths.Where(kv => kv.Value >= threshold).Select(kv => kv.Key).ToList();
    }
}
