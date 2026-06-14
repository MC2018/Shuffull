using Shuffull.Core.Authentication;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Tests.Tools;

/// <summary>
/// Deterministic <see cref="IAuthTokenGenerator"/> for handler tests. Encodes the user id into the
/// token so assertions can confirm the token was issued for the right user, and records the last
/// user it was asked to tokenize.
/// </summary>
public class FakeAuthTokenGenerator : IAuthTokenGenerator
{
    public static readonly DateTime FixedExpiration = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public User? LastUser { get; private set; }

    public AuthToken Generate(User user)
    {
        LastUser = user;
        return new AuthToken($"token-for-{user.UserId}", FixedExpiration);
    }
}
