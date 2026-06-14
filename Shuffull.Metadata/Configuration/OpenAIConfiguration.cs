using System.Text.Json.Serialization;

namespace Shuffull.Metadata.Configuration;

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
