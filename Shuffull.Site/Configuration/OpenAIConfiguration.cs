using Shuffull.Metadata.Configuration;
using Shuffull.Metadata.Services.AI;

namespace Shuffull.Site.Configuration;

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

        collection.AddSingleton<IAIService>(new OpenAIService(openAIConfig));

        return collection;
    }
}
