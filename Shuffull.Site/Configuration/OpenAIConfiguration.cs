using System.Text.Json.Serialization;

namespace Shuffull.Site.Configuration
{
    public class OpenAIConfiguration
    {
        [JsonIgnore]
        public const string OpenAIConfigurationSection = "OpenAI";
        public bool Enabled { get; set; } = false;
        public string ApiKey { get; set; } = string.Empty;
        public string ModelName { get; set; } = string.Empty;
        public string InstructionFile { get; set; } = string.Empty;
    }
}
