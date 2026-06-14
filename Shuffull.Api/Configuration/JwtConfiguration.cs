using System.Text.Json.Serialization;

namespace Shuffull.Api.Configuration;

public class JwtConfiguration
{
    [JsonIgnore]
    public const string JwtConfigurationSection = "Jwt";
    public string Secret { get; set; } = string.Empty;
}
