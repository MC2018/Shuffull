using Microsoft.IdentityModel.Tokens;
using Shuffull.Api.Configuration;
using Shuffull.Core.Models.Database;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Shuffull.Api.Tools.Authorization;

public class JwtHelper
{
    public const string UserIdClaim = "UserId";

    /// <summary>The user's <see cref="User.SecurityStamp"/> when the token was issued; absent while it was null.</summary>
    public const string SecurityStampClaim = "SecurityStamp";

    private readonly JwtConfiguration _configuration;

    public JwtHelper(IServiceProvider services)
    {
        _configuration = JwtConfiguration.Load(services.GetRequiredService<IConfiguration>());
    }

    public string GenerateJwtToken(User user, DateTime expiration)
    {
        var claims = new List<Claim> { new(UserIdClaim, user.UserId) };
        if (user.SecurityStamp != null)
        {
            claims.Add(new Claim(SecurityStampClaim, user.SecurityStamp));
        }

        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(_configuration.Secret);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expiration,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    /// <summary>
    /// True while a token is still good for <paramref name="user"/>: the stamp it carries equals the user's current
    /// one, or the token has none and neither does the user (every token and user from before stamps existed).
    /// </summary>
    public static bool StampMatches(JwtSecurityToken token, User user)
    {
        var tokenStamp = token.Claims.FirstOrDefault(claim => claim.Type == SecurityStampClaim)?.Value;
        return string.Equals(tokenStamp, user.SecurityStamp, StringComparison.Ordinal);
    }
}
