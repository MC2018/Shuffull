using System.Text.Json.Serialization;

namespace Shuffull.Api.Configuration;

public class JwtConfiguration
{
    [JsonIgnore]
    public const string JwtConfigurationSection = "Jwt";
    public string Secret { get; set; } = string.Empty;

    /// <summary>
    /// Reads the Jwt section, or throws if it or its secret is missing. Without this the API started fine and
    /// failed at the first login: a missing section as a NullReferenceException, and an unset JWT_SECRET (which
    /// compose passes as an empty <c>Jwt__Secret</c>) as a zero-length signing key.
    /// </summary>
    public static JwtConfiguration Load(IConfiguration configuration)
    {
        var jwt = configuration.GetSection(JwtConfigurationSection).Get<JwtConfiguration>()
            ?? throw new InvalidOperationException("Jwt configuration is not set.");
        if (string.IsNullOrWhiteSpace(jwt.Secret))
        {
            throw new InvalidOperationException("Jwt:Secret is empty. Set JWT_SECRET.");
        }
        return jwt;
    }
}
