using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shuffull.Core.Tools;
using Shuffull.Metadata.Tools;

namespace Shuffull.Core.Tests.Tools;

/// <summary>
/// Tier → model resolution from raw configuration. The cases that matter are the blank ones: compose renders an
/// unset variable as <c>KEY=</c>, which arrives as "" rather than null, and prod's strong tier resolved to ""
/// that way (Shuffull#36).
/// </summary>
public class TierModelConfigurationTest
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value))
            .Build();

    private static readonly ModelStrengths Strengths = new(new Dictionary<string, int>
    {
        ["weak-model"] = 10,
        ["strong-model"] = 30,
    });

    [Fact]
    public void ResolvesEachTierThroughItsProvider()
    {
        var config = Config(
            ("AI:Tiers:Weak", "Meta"), ("AI:Tiers:Strong", "OpenAI"),
            ("AI:Meta:WeakModelName", "weak-model"), ("AI:OpenAI:StrongModelName", "strong-model"));

        Assert.Equal("weak-model", TierModelConfiguration.ResolveModel(config, TierModelConfiguration.Weak));
        Assert.Equal("strong-model", TierModelConfiguration.ResolveModel(config, TierModelConfiguration.Strong));
    }

    [Fact]
    public void UnsetTiers_DefaultToOpenAI()
    {
        var config = Config(("AI:OpenAI:StrongModelName", "strong-model"), ("AI:Meta:StrongModelName", "other"));

        Assert.Equal("strong-model", TierModelConfiguration.ResolveModel(config, TierModelConfiguration.Strong));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void BlankTier_DefaultsToOpenAI(string blank)
    {
        var config = Config(("AI:Tiers:Strong", blank), ("AI:OpenAI:StrongModelName", "strong-model"));

        Assert.Equal("strong-model", TierModelConfiguration.ResolveModel(config, TierModelConfiguration.Strong));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void BlankStrongModel_FallsBackToLegacyModelName(string? blank)
    {
        // The prod shape: AI__OpenAI__StrongModelName= (empty) — a `??` chain stops at the "" and never reaches
        // ModelName.
        var config = Config(("AI:OpenAI:StrongModelName", blank), ("AI:OpenAI:ModelName", "strong-model"));

        Assert.Equal("strong-model", TierModelConfiguration.ResolveModel(config, TierModelConfiguration.Strong));
    }

    [Fact]
    public void BlankWeakModel_FallsBackToTheProvidersStrongModel()
    {
        var config = Config(("AI:OpenAI:WeakModelName", ""), ("AI:OpenAI:StrongModelName", "strong-model"));

        Assert.Equal("strong-model", TierModelConfiguration.ResolveModel(config, TierModelConfiguration.Weak));
    }

    [Fact]
    public void EverythingBlank_ResolvesToNull_NotEmptyString()
    {
        var config = Config(("AI:OpenAI:StrongModelName", ""), ("AI:OpenAI:WeakModelName", ""));

        Assert.Null(TierModelConfiguration.ResolveModel(config, TierModelConfiguration.Strong));
        Assert.Null(TierModelConfiguration.ResolveModel(config, TierModelConfiguration.Weak));
    }

    [Fact]
    public void RegisteredModels_LogNothing()
    {
        var logger = new ListLogger();
        var config = Config(("AI:OpenAI:WeakModelName", "weak-model"), ("AI:OpenAI:StrongModelName", "strong-model"));

        var warned = TierModelConfiguration.WarnOnUnregisteredModels(config, Strengths, logger);

        Assert.Empty(warned);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void BlankStrongModel_LogsAWarning()
    {
        // Exactly prod on 2026-10-03: weak tier on Meta and registered, strong tier on OpenAI with an empty model.
        var logger = new ListLogger();
        var config = Config(
            ("AI:Tiers:Weak", "Meta"), ("AI:Tiers:Strong", "OpenAI"),
            ("AI:Meta:WeakModelName", "weak-model"), ("AI:OpenAI:StrongModelName", ""));

        var warned = TierModelConfiguration.WarnOnUnregisteredModels(config, Strengths, logger);

        Assert.Equal([TierModelConfiguration.Strong], warned);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("Strong tier has no model", entry.Message);
        Assert.Contains("AI:OpenAI", entry.Message);
    }

    [Fact]
    public void UnregisteredModel_LogsAWarningNamingIt()
    {
        var logger = new ListLogger();
        var config = Config(("AI:OpenAI:WeakModelName", "weak-model"), ("AI:OpenAI:StrongModelName", "gpt-9-unlisted"));

        var warned = TierModelConfiguration.WarnOnUnregisteredModels(config, Strengths, logger);

        Assert.Equal([TierModelConfiguration.Strong], warned);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("'gpt-9-unlisted'", entry.Message);
        Assert.Contains("AI:ModelStrengths", entry.Message);
    }

    [Fact]
    public void BothTiersUnregistered_WarnsForEach()
    {
        var logger = new ListLogger();

        var warned = TierModelConfiguration.WarnOnUnregisteredModels(Config(), Strengths, logger);

        Assert.Equal([TierModelConfiguration.Weak, TierModelConfiguration.Strong], warned);
        Assert.Equal(2, logger.Entries.Count);
    }

    private sealed class ListLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
