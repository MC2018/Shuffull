using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Shuffull.Api.Configuration;
using Shuffull.Api.Hubs;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence;
using System.IdentityModel.Tokens.Jwt;
using System.Text;

namespace Shuffull.Api.Tools.Authorization;

/// <summary>
/// Runs the middleware for the JSON Web Tokens
/// </summary>
public class JwtMiddleware
{
    private readonly RequestDelegate _next;
    private readonly JwtConfiguration _configuration;
    private readonly ILogger<JwtMiddleware> _logger;

    /// <summary>
    /// Set in <c>HttpContext.Items</c> when the token was valid but the user lookup failed (the database is down), so
    /// [Authorize] can answer 503 instead of 401.
    /// </summary>
    public const string AuthUnavailableKey = "AuthUnavailable";

    /// <summary>The <c>HttpContext.Items</c> key the signed-in <see cref="User"/> is attached under.</summary>
    public const string UserKey = "User";

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="next">Next delegate</param>
    /// <param name="services">Service provider</param>
    public JwtMiddleware(RequestDelegate next, IServiceProvider services)
    {
        _next = next;
        _configuration = JwtConfiguration.Load(services.GetRequiredService<IConfiguration>());
        _logger = services.GetRequiredService<ILogger<JwtMiddleware>>();
    }

    /// <summary>
    /// Checks for an Authorization header on any API call
    /// </summary>
    /// <param name="httpContext">HTTP context</param>
    /// <param name="dbContext">Database context</param>
    /// <returns></returns>
    public async Task Invoke(HttpContext httpContext, ShuffullContext dbContext)
    {
        var token = httpContext.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last()
                    ?? PlaybackHubQueryToken(httpContext);

        if (token != null)
        {
            await AttachUserToContext(httpContext, dbContext, token);
        }

        await _next(httpContext);
    }

    /// <summary>
    /// The token from <c>?access_token=</c>, on the playback hub's path only (Shuffull#6). A browser's WebSocket can't
    /// carry an Authorization header, so SignalR clients put the token in the query there. Nowhere else: a token in a
    /// URL ends up in logs and history, and shuffull-workspace#58 is removing the one other query fallback (login), so
    /// don't widen this to other paths. nlog.config drops log lines that contain <c>access_token=</c>.
    /// </summary>
    private static string? PlaybackHubQueryToken(HttpContext httpContext)
    {
        if (!httpContext.Request.Path.StartsWithSegments(PlaybackHub.Path))
        {
            return null;
        }
        string? token = httpContext.Request.Query["access_token"];
        return string.IsNullOrEmpty(token) ? null : token;
    }

    /// <summary>
    /// Attaches the user to the context. A token that doesn't validate, or names a user who no longer exists, leaves
    /// the request unauthenticated (401 on an [Authorize] route). A database failure during the lookup instead sets
    /// <see cref="AuthUnavailableKey"/>, which [Authorize] answers with 503: the session may be fine, the server just
    /// can't check it, and a client that logs out on 401 must not log out because Postgres is restarting.
    /// </summary>
    /// <param name="httpContext">HTTP context</param>
    /// <param name="dbContext">Database context</param>
    /// <param name="token">Token</param>
    /// <returns></returns>
    private async Task AttachUserToContext(HttpContext httpContext, ShuffullContext dbContext, string token)
    {
        TokenValidationResult tokenValidation;
        try
        {
            tokenValidation = await new JwtSecurityTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(_configuration.Secret)),
                ValidateIssuer = false,
                ValidateAudience = false,
                ClockSkew = TimeSpan.Zero
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "JWT validation failed; request will be treated as unauthenticated.");
            return;
        }

        if (!tokenValidation.IsValid || tokenValidation.SecurityToken is not JwtSecurityToken jwtToken)
        {
            _logger.LogWarning(tokenValidation.Exception, "JWT validation failed; request will be treated as unauthenticated.");
            return;
        }

        var userId = jwtToken.Claims.FirstOrDefault(x => x.Type == JwtHelper.UserIdClaim)?.Value;
        if (userId == null)
        {
            _logger.LogWarning("JWT carries no {Claim} claim; request will be treated as unauthenticated.", JwtHelper.UserIdClaim);
            return;
        }

        User? user;
        try
        {
            user = await dbContext.Users.Where(x => x.UserId == userId).AsNoTracking().FirstOrDefaultAsync(httpContext.RequestAborted);
        }
        catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
        {
            // The client went away; nobody reads the answer.
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not look up user {UserId} for a valid token; [Authorize] routes will answer 503.", userId);
            httpContext.Items[AuthUnavailableKey] = true;
            return;
        }

        if (user == null)
        {
            _logger.LogInformation("Token for user {UserId} names no existing user; request will be treated as unauthenticated.", userId);
            return;
        }

        // A password change since the token was issued replaced the stamp, so the token no longer counts.
        if (!JwtHelper.StampMatches(jwtToken, user))
        {
            _logger.LogInformation("Token for user {UserId} predates their last password change; request will be treated as unauthenticated.", userId);
            return;
        }

        httpContext.Items[UserKey] = user;
    }
}
