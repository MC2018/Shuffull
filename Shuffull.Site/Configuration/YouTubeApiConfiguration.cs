using System.Text.Json.Serialization;

namespace Shuffull.Site.Configuration;

public class YouTubeApiConfiguration
{
    [JsonIgnore]
    public const string YouTubeApiConfigurationSection = "YouTubeApi";
    
    public string ApiKey { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = string.Empty;
}
