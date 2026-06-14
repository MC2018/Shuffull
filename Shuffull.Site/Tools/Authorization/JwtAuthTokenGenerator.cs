using Shuffull.Core.Authentication;
using Shuffull.Core.Models.Database;

namespace Shuffull.Site.Tools.Authorization;

/// <summary>
/// Host-side implementation of <see cref="IAuthTokenGenerator"/>. Wraps the existing
/// <see cref="JwtHelper"/> and owns the token lifetime policy (30 days, matching the legacy
/// UserController) so Shuffull.Core handlers stay free of JWT infrastructure.
/// </summary>
public class JwtAuthTokenGenerator : IAuthTokenGenerator
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromDays(30);

    private readonly JwtHelper _jwtHelper;

    public JwtAuthTokenGenerator(JwtHelper jwtHelper)
    {
        _jwtHelper = jwtHelper;
    }

    public AuthToken Generate(User user)
    {
        var expiration = DateTime.UtcNow.Add(TokenLifetime);
        var token = _jwtHelper.GenerateJwtToken(user, expiration);
        return new AuthToken(token, expiration);
    }
}
