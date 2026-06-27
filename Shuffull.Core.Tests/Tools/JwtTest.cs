using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Shuffull.Api.Tools.Authorization;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Tests.Tools;

/// <summary>
/// Exercises the JWT helpers with an arbitrary in-test secret (no real production key needed): a token is
/// generated, then validated/decoded with the same key to confirm the claims and lifetime round-trip.
/// </summary>
public class JwtTest
{
    // 32+ ASCII chars so the HMAC-SHA256 signing key meets the 128-bit minimum.
    private const string TestSecret = "test-secret-key-that-is-long-enough-123456";

    private static JwtHelper CreateHelper(string secret = TestSecret)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = secret,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        return new JwtHelper(services.BuildServiceProvider());
    }

    private static User MakeUser(string userId = "user-42")
        => new() { UserId = userId, Username = "tester", Version = DateTime.UtcNow, ServerHash = "hash" };

    private static (string UserId, DateTime ValidTo) ReadToken(string token, string secret = TestSecret)
    {
        var handler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(secret);
        var principal = handler.ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ValidateIssuer = false,
            ValidateAudience = false,
            ClockSkew = TimeSpan.Zero,
        }, out var validated);

        var jwt = (JwtSecurityToken)validated;
        return (principal.FindFirst("UserId")!.Value, jwt.ValidTo);
    }

    [Fact]
    public void GenerateJwtToken_EmbedsUserIdClaim()
    {
        var helper = CreateHelper();
        var expiration = DateTime.UtcNow.AddHours(1);

        var token = helper.GenerateJwtToken(MakeUser("user-99"), expiration);
        var (userId, _) = ReadToken(token);

        Assert.Equal("user-99", userId);
    }

    [Fact]
    public void GenerateJwtToken_HonorsRequestedExpiration()
    {
        var helper = CreateHelper();
        var expiration = DateTime.UtcNow.AddMinutes(30);

        var token = helper.GenerateJwtToken(MakeUser(), expiration);
        var (_, validTo) = ReadToken(token);

        // JWT exp is whole-second precision, so allow a couple of seconds of slack.
        Assert.True(Math.Abs((validTo - expiration).TotalSeconds) < 2);
    }

    [Fact]
    public void GenerateJwtToken_ProducesThreePartJwt()
    {
        var helper = CreateHelper();

        var token = helper.GenerateJwtToken(MakeUser(), DateTime.UtcNow.AddHours(1));

        Assert.Equal(3, token.Split('.').Length);
    }

    [Fact]
    public void GenerateJwtToken_SignedWithDifferentSecretFailsValidation()
    {
        var helper = CreateHelper("first-secret-key-that-is-long-enough-1234567");

        var token = helper.GenerateJwtToken(MakeUser(), DateTime.UtcNow.AddHours(1));

        Assert.ThrowsAny<SecurityTokenException>(() =>
            ReadToken(token, "a-totally-different-secret-key-1234567890"));
    }

    [Fact]
    public void JwtAuthTokenGenerator_IssuesTokenWithApproximately30DayLifetime()
    {
        var generator = new JwtAuthTokenGenerator(CreateHelper());

        var before = DateTime.UtcNow;
        var authToken = generator.Generate(MakeUser("user-7"));

        Assert.False(string.IsNullOrWhiteSpace(authToken.Token));
        // Lifetime policy is 30 days; allow a small window around "now".
        var expectedExpiration = before.AddDays(30);
        Assert.True(Math.Abs((authToken.Expiration - expectedExpiration).TotalMinutes) < 1);
    }

    [Fact]
    public void JwtAuthTokenGenerator_TokenDecodesToTheSameUser()
    {
        var generator = new JwtAuthTokenGenerator(CreateHelper());

        var authToken = generator.Generate(MakeUser("round-trip-user"));
        var (userId, _) = ReadToken(authToken.Token);

        Assert.Equal("round-trip-user", userId);
    }
}
