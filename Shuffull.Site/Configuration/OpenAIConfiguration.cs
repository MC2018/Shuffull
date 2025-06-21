using Shuffull.Site.Tools.AI;
using Shuffull.Site.Tools;
using System.Text.Json.Serialization;

namespace Shuffull.Site.Configuration;

public class OpenAIConfiguration
{
    [JsonIgnore]
    public const string OpenAIConfigurationSection = "AI:OpenAI";
    public string ApiKey { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string InstructionFile { get; set; } = string.Empty;
    public string ApiEndpoint { get; set; } = string.Empty;

    public class SupportedApiEndpoints
    {
        // Lowercase for consistency
        public const string ChatCompletions = "chat/completions";
        public const string Responses = "responses";
        public static readonly string[] All = { ChatCompletions, Responses };
    }
}

public static class OpenAIConfigurationExtensions
{
    public static IServiceCollection AddOpenAIService(this IServiceCollection collection, ConfigurationManager configurationManager)
    {
        var openAIConfig = configurationManager.GetSection(OpenAIConfiguration.OpenAIConfigurationSection).Get<OpenAIConfiguration>();

        if (openAIConfig == null)
        {
            throw new InvalidOperationException($"OpenAI configuration section '{OpenAIConfiguration.OpenAIConfigurationSection}' is missing or invalid.");
        }
        else if (string.IsNullOrEmpty(openAIConfig.ApiKey))
        {
            throw new InvalidOperationException("OpenAI API key is not set in the configuration.");
        }
        else if (string.IsNullOrEmpty(openAIConfig.ModelName))
        {
            throw new InvalidOperationException("OpenAI model name is not set in the configuration.");
        }
        else if (!OpenAIConfiguration.SupportedApiEndpoints.All.Contains(openAIConfig.ApiEndpoint))
        {
            throw new NotSupportedException($"OpenAI API endpoint '{openAIConfig.ApiEndpoint}' is not supported. Supported endpoints are: {string.Join(", ", OpenAIConfiguration.SupportedApiEndpoints.All)}.");
        }

        collection.AddSingleton<IAIManager, OpenAIManager>();

        return collection;
    }
}