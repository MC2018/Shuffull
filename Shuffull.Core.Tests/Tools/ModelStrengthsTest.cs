using Shuffull.Core.Tools;

namespace Shuffull.Core.Tests.Tools;

/// <summary>
/// Locks in the fail-safe staleness semantics: null/unknown models resolve to strength 0, staleness is a
/// strict less-than against the CURRENT strong model, and an unregistered current model can never trigger a
/// re-tag (so forgetting to register a newly-adopted model is a no-op, not a whole-library re-tag).
/// </summary>
public class ModelStrengthsTest
{
    private static ModelStrengths Map() => new(new Dictionary<string, int>
    {
        ["gpt-5.4-mini"] = 10,
        ["gpt-5.5"] = 30,
    });

    [Fact]
    public void WeakerTagModel_IsStale()
    {
        Assert.True(Map().IsStale("gpt-5.4-mini", "gpt-5.5"));
    }

    [Fact]
    public void EqualOrStrongerTagModel_IsNotStale()
    {
        var map = Map();
        Assert.False(map.IsStale("gpt-5.5", "gpt-5.5"));
        Assert.False(map.IsStale("gpt-5.5", "gpt-5.4-mini"));
    }

    [Fact]
    public void NullTagModel_IsStale_WhenCurrentIsRegistered()
    {
        // The pre-provenance library (TagModel = null) is stale by definition once a registered model is current.
        Assert.True(Map().IsStale(null, "gpt-5.4-mini"));
    }

    [Fact]
    public void UnregisteredCurrentModel_NeverMarksStale()
    {
        // Fail-safe: a current strong model missing from the map (strength 0) must not trigger a mass re-tag,
        // even against null/unknown tag models (0 < 0 is false).
        var map = Map();
        Assert.False(map.IsStale(null, "gpt-6-brand-new"));
        Assert.False(map.IsStale("gpt-5.4-mini", "gpt-6-brand-new"));
        Assert.False(map.IsStale(null, null));
    }

    [Fact]
    public void UnknownTagModel_TreatedAsWeakest()
    {
        Assert.True(Map().IsStale("some-retired-model", "gpt-5.5"));
    }

    [Fact]
    public void Lookup_IsCaseInsensitive()
    {
        var map = Map();
        Assert.Equal(30, map.GetStrength("GPT-5.5"));
        Assert.False(map.IsStale("GPT-5.5", "gpt-5.5"));
    }

    [Fact]
    public void Knows_ReportsRegistration()
    {
        var map = Map();
        Assert.True(map.Knows("gpt-5.5"));
        Assert.False(map.Knows("gpt-6-brand-new"));
        Assert.False(map.Knows(null));
    }

    [Fact]
    public void EmptyOrNullConfig_IsSafe()
    {
        var empty = new ModelStrengths();
        Assert.Equal(0, empty.GetStrength("anything"));
        Assert.False(empty.IsStale(null, "gpt-5.5"));
    }
}
