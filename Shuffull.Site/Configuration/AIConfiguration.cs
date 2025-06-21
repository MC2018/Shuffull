using Shuffull.Site.Tools;
using Shuffull.Site.Tools.AI;
using System.Text.Json.Serialization;

namespace Shuffull.Site.Configuration;

public class AIConfiguration
{
    [JsonIgnore]
    public const string AIConfigurationSection = "AI";
    public bool Enabled { get; set; } = false;
    public string Platform { get; set; } = string.Empty;

    // Constants for supported AI platforms. Keep lowercase for consistency.
    public class Platforms
    {
        public const string OpenAIPlatform = "openai";
    }
}

public static class AIConfigurationExtensions
{
    public static IServiceCollection TryAddApiService(this IServiceCollection collection, ConfigurationManager configurationManager)
    {
        var aiConfig = configurationManager.GetSection(AIConfiguration.AIConfigurationSection).Get<AIConfiguration>();

        if (aiConfig == null)
        {
            throw new InvalidOperationException($"AI configuration section '{AIConfiguration.AIConfigurationSection}' is missing or invalid.");
        }
        else if (!aiConfig.Enabled)
        {
            return collection;
        }

        switch (aiConfig.Platform.ToLower())
        {
            case AIConfiguration.Platforms.OpenAIPlatform:
                collection.AddOpenAIService(configurationManager);
                break;
            case null:
            case "":
                throw new InvalidOperationException("AI platform is not set in the configuration.");
            default:
                throw new NotSupportedException($"AI platform '{aiConfig.Platform}' is not supported.");
        }

        return collection;
    }
}
