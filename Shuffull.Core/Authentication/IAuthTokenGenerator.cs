using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Authentication;

/// <summary>
/// Issues an authentication token for a user. The concrete implementation (JWT signing, secret,
/// lifetime policy) lives in the host (Shuffull.Site) so Shuffull.Core stays free of web/JWT
/// infrastructure; handlers depend only on this abstraction.
/// </summary>
public interface IAuthTokenGenerator
{
    AuthToken Generate(User user);
}

/// <summary>A signed token plus the instant it expires.</summary>
public record AuthToken(string Token, DateTime Expiration);
