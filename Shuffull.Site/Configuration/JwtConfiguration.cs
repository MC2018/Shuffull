using System.Text.Json.Serialization;

namespace Shuffull.Site.Configuration
{
    public class JwtConfiguration
    {
        [JsonIgnore]
        public const string JwtConfigurationSection = "Jwt";
        public string Secret { get; set; } = string.Empty;
    }
}
