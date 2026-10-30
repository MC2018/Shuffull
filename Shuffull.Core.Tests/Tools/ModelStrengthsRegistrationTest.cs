using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Shuffull.Core.Tests.Infrastructure;
using Shuffull.Metadata.Tools;

namespace Shuffull.Core.Tests.Tools;

/// <summary>
/// Every model name this repo ships as configuration must be in <c>AI:ModelStrengths</c>. An unregistered model
/// scores 0, and every consumer of the ladder fails safe and silent on a 0: nothing reads stale, so likes quietly
/// stop upgrading tags. That is Shuffull#36, and this test would have caught it at commit time.
///
/// <para>It checks what the repo ships: the model names in Shuffull.Api/appsettings.json and the <c>*_MODEL</c>
/// variables in .env.example. A model set only in a deployment's env file is caught at startup instead, by
/// TierModelConfiguration.WarnOnUnregisteredModels.</para>
/// </summary>
public class ModelStrengthsRegistrationTest
{
    private static readonly string[] ModelKeys = ["StrongModelName", "WeakModelName", "ModelName"];

    [Fact]
    public void EveryShippedModelName_IsInModelStrengths()
    {
        var root = RepoPaths.Root();
        var appsettings = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(root, "Shuffull.Api", "appsettings.json"), optional: false)
            .Build();
        var strengths = new ModelStrengths(appsettings.GetSection("AI:ModelStrengths").Get<Dictionary<string, int>>());

        var models = AppSettingsModels(appsettings).Concat(EnvExampleModels(Path.Combine(root, ".env.example"))).ToList();

        Assert.NotEmpty(models);
        Assert.Empty(models.Where(m => !strengths.Knows(m.Model)).Select(m => $"{m.Model} ({m.Source})"));
    }

    /// <summary>The model names under each provider section, AI:{provider}:{Strong|Weak}ModelName and the legacy ModelName.</summary>
    private static IEnumerable<(string Model, string Source)> AppSettingsModels(IConfiguration appsettings) =>
        from provider in appsettings.GetSection("AI").GetChildren()
        from key in ModelKeys
        let model = provider[key]
        where !string.IsNullOrWhiteSpace(model)
        select (model!, $"appsettings.json AI:{provider.Key}:{key}");

    private static IEnumerable<(string Model, string Source)> EnvExampleModels(string path) =>
        from line in File.ReadAllLines(path)
        let match = Regex.Match(line, @"^\s*([A-Z0-9_]+_MODEL)\s*=\s*(\S*)")
        where match.Success && match.Groups[2].Value.Length > 0
        select (match.Groups[2].Value, $".env.example {match.Groups[1].Value}");
}
