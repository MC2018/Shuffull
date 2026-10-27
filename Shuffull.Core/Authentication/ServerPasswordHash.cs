using System.Security.Cryptography;
using System.Text;
using Shuffull.Core.Models.Database;
using Shuffull.Shared.Tools;

namespace Shuffull.Core.Authentication;

/// <summary>
/// The server's hash of the credential a client sends. The client sends its own argon2 of "username;password"
/// (<see cref="Hasher.Argon2Hash(string)"/>); the server hashes that again with a random salt per user, so two
/// accounts never share a stored hash and a leaked table can't be attacked with one precomputed table.
/// Accounts created before per-user salts have no <see cref="User.ServerHashSalt"/> and still verify against the
/// old shared salt until <see cref="Set"/> re-hashes them at their next login.
/// </summary>
public static class ServerPasswordHash
{
    private const int SaltBytes = 16;

    /// <summary>Gives <paramref name="user"/> a fresh salt and stores the hash of <paramref name="clientHash"/> under it.</summary>
    public static void Set(User user, string clientHash)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        user.ServerHashSalt = Convert.ToHexString(salt).ToLowerInvariant();
        user.ServerHash = Hasher.Argon2Hash(clientHash, salt);
    }

    public static bool Verify(User user, string clientHash)
    {
        var expected = user.ServerHashSalt == null
            ? Hasher.Argon2Hash(clientHash)
            : Hasher.Argon2Hash(clientHash, Convert.FromHexString(user.ServerHashSalt));

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(user.ServerHash));
    }

    /// <summary>
    /// Spends the same argon2 work as <see cref="Verify"/> when there is no user to check, so the response time
    /// doesn't tell a caller whether the username exists.
    /// </summary>
    public static void VerifyNobody(string clientHash)
    {
        Hasher.Argon2Hash(clientHash, new byte[SaltBytes]);
    }
}
