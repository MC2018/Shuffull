using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Shuffull.Api.Tools.Authorization;
using Shuffull.Core.Authentication;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Tools;

/// <summary>
/// Exercises the JWT helpers with an arbitrary in-test secret (no real production key needed): a token is
/// generated, then validated/decoded with the same key to confirm the claims and lifetime round-trip.
/// </summary>
public class JwtTest
{
    // 32+ ASCII chars so the HMAC-SHA256 signing key meets the 128-bit minimum.
    private const string TestSecret = "test-secret-key-that-is-long-enough-123456";

    private static IServiceProvider CreateServices(string secret = TestSecret)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = secret,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        return services.BuildServiceProvider();
    }

    private static JwtHelper CreateHelper(string secret = TestSecret) => new(CreateServices(secret));

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

    // Both used to construct fine and fail at the first login instead: a NullReferenceException for a missing
    // section, a zero-length signing key for an unset JWT_SECRET (compose passes it as Jwt__Secret="").
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void JwtHelperAndMiddleware_WithoutASecret_FailAtConstruction(string? secret)
    {
        var values = new Dictionary<string, string?>();
        if (secret != null)
        {
            values["Jwt:Secret"] = secret;
        }
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        var provider = services.BuildServiceProvider();

        var helperError = Assert.Throws<InvalidOperationException>(() => new JwtHelper(provider));
        var middlewareError = Assert.Throws<InvalidOperationException>(() => new JwtMiddleware(_ => Task.CompletedTask, provider));
        Assert.Contains("Jwt", helperError.Message);
        Assert.Equal(helperError.Message, middlewareError.Message);
    }

    /// <summary>Runs <see cref="JwtMiddleware"/> on a request bearing <paramref name="token"/>; returns the user it attached.</summary>
    private static async Task<User?> AuthenticateAsync(DatabaseFixture database, string token)
    {
        var middleware = new JwtMiddleware(_ => Task.CompletedTask, CreateServices());
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {token}";

        await middleware.Invoke(context, database.CreateContext());

        return context.Items[JwtMiddleware.UserKey] as User;
    }

    private static async Task<User> SeedUserAsync(DatabaseFixture database)
    {
        var user = MakeUser(Guid.NewGuid().ToString());
        ServerPasswordHash.Set(user, "old-hash");
        database.Context.Users.Add(user);
        await database.Context.SaveChangesAsync();
        return user;
    }

    private static async Task ChangePasswordAsync(DatabaseFixture database, User user, string newHash)
    {
        ServerPasswordHash.Change(user, newHash);
        await database.Context.SaveChangesAsync();
    }

    /// <summary>What the API issued before security stamps: a token whose only claim is the user id.</summary>
    private static string PreStampToken(string userId)
    {
        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[] { new Claim("UserId", userId) }),
            Expires = DateTime.UtcNow.AddDays(30),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.ASCII.GetBytes(TestSecret)), SecurityAlgorithms.HmacSha256Signature),
        }));
    }

    [Fact]
    public void GenerateJwtToken_WithoutAStamp_CarriesNoStampClaim()
    {
        var token = new JwtSecurityTokenHandler().ReadJwtToken(CreateHelper().GenerateJwtToken(MakeUser(), DateTime.UtcNow.AddHours(1)));

        Assert.DoesNotContain(token.Claims, claim => claim.Type == JwtHelper.SecurityStampClaim);
    }

    // The deploy itself must not sign anyone out: every token already in an app has no stamp, and neither does
    // any user until they change their password.
    [Fact]
    public async Task Middleware_AcceptsATokenFromBeforeStamps_WhileThePasswordIsUnchanged()
    {
        using var database = new DatabaseFixture();
        var user = await SeedUserAsync(database);

        var attached = await AuthenticateAsync(database, PreStampToken(user.UserId));

        Assert.Equal(user.UserId, attached?.UserId);
    }

    [Fact]
    public async Task Middleware_RejectsEveryEarlierToken_AfterAPasswordChange()
    {
        using var database = new DatabaseFixture();
        var helper = CreateHelper();
        var user = await SeedUserAsync(database);
        var preStampToken = PreStampToken(user.UserId);
        var beforeFirstChange = helper.GenerateJwtToken(user, DateTime.UtcNow.AddDays(30));

        await ChangePasswordAsync(database, user, "second-hash");
        var betweenChanges = helper.GenerateJwtToken(user, DateTime.UtcNow.AddDays(30));
        Assert.NotNull(await AuthenticateAsync(database, betweenChanges));

        await ChangePasswordAsync(database, user, "third-hash");
        var afterSecondChange = helper.GenerateJwtToken(user, DateTime.UtcNow.AddDays(30));

        Assert.Null(await AuthenticateAsync(database, preStampToken));
        Assert.Null(await AuthenticateAsync(database, beforeFirstChange));
        Assert.Null(await AuthenticateAsync(database, betweenChanges));
        Assert.Equal(user.UserId, (await AuthenticateAsync(database, afterSecondChange))?.UserId);
    }

    /// <summary>
    /// The status an [Authorize] route answers a request bearing <paramref name="token"/> with, after
    /// <see cref="JwtMiddleware"/> ran against <paramref name="dbContext"/>; 200 when the filter lets it through.
    /// </summary>
    private static async Task<int> AuthorizedStatusAsync(ShuffullContext dbContext, string token)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {token}";
        int? status = null;
        var middleware = new JwtMiddleware(httpContext =>
        {
            var filterContext = new AuthorizationFilterContext(
                new ActionContext(httpContext, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>());
            new AuthorizeAttribute().OnAuthorization(filterContext);
            status = filterContext.Result is JsonResult result ? result.StatusCode : StatusCodes.Status200OK;
            return Task.CompletedTask;
        }, CreateServices());

        await middleware.Invoke(context, dbContext);

        Assert.NotNull(status);
        return status.Value;
    }

    private static string SignedToken(Claim claim) => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Subject = new ClaimsIdentity(new[] { claim }),
        Expires = DateTime.UtcNow.AddHours(1),
        SigningCredentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.ASCII.GetBytes(TestSecret)), SecurityAlgorithms.HmacSha256Signature),
    }));

    /// <summary>A context whose Postgres refuses the connection, as it does while restarting or after an OOM kill.</summary>
    private static ShuffullContext UnreachablePostgres() => new(new DbContextOptionsBuilder<ShuffullContext>()
        .UseNpgsql("Host=127.0.0.1;Port=1;Database=shuffull;Username=nobody;Password=x;Timeout=2")
        .Options);

    // Shuffull#108: a 401 has to mean "this session is gone", so the app can log out on it. A database that is down
    // says nothing about the session, so it must not answer 401 too.
    [Fact]
    public async Task Authorize_AnswersAValidToken_With503_WhenTheDatabaseIsDown()
    {
        var token = CreateHelper().GenerateJwtToken(MakeUser(), DateTime.UtcNow.AddHours(1));
        await using var unreachable = UnreachablePostgres();

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, await AuthorizedStatusAsync(unreachable, token));
    }

    [Fact]
    public async Task Authorize_AnswersABadToken_With401_EvenWhenTheDatabaseIsDown()
    {
        var badTokens = new[]
        {
            "not-a-jwt",
            CreateHelper("a-totally-different-secret-key-1234567890").GenerateJwtToken(MakeUser(), DateTime.UtcNow.AddHours(1)),
            PreStampToken("user-42")[..^2], // a cut signature
            SignedToken(new Claim("NotTheUserId", "user-42")), // validly signed, but names no user
        };
        await using var unreachable = UnreachablePostgres();

        foreach (var token in badTokens)
        {
            Assert.Equal(StatusCodes.Status401Unauthorized, await AuthorizedStatusAsync(unreachable, token));
        }
        // Expired: validation rejects it before any lookup, so it never reaches the database either.
        var expired = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[] { new Claim("UserId", "user-42") }),
            NotBefore = DateTime.UtcNow.AddHours(-2),
            Expires = DateTime.UtcNow.AddHours(-1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.ASCII.GetBytes(TestSecret)), SecurityAlgorithms.HmacSha256Signature),
        }));
        Assert.Equal(StatusCodes.Status401Unauthorized, await AuthorizedStatusAsync(unreachable, expired));
    }

    [Fact]
    public async Task Authorize_AnswersAValidTokenForADeletedUser_With401()
    {
        using var database = new DatabaseFixture();
        var user = await SeedUserAsync(database);
        var token = CreateHelper().GenerateJwtToken(user, DateTime.UtcNow.AddDays(30));
        Assert.Equal(StatusCodes.Status200OK, await AuthorizedStatusAsync(database.CreateContext(), token));

        database.Context.Users.Remove(user);
        await database.Context.SaveChangesAsync();

        Assert.Equal(StatusCodes.Status401Unauthorized, await AuthorizedStatusAsync(database.CreateContext(), token));
    }
}
