using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
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

    // Argon2id settings for the server's hash. They start equal to the client hash's (Hasher.Argon2Hash), which is
    // what every stored hash was made with. Unlike the client's they can change, but only for new hashes: an
    // account's hash verifies only under the settings it was made with.
    private const int MemorySizeKb = 16384;
    private const int DegreeOfParallelism = 4;
    private const int Iterations = 5;
    private const int HashBytes = 16;

    /// <summary>The salt every server hash used before per-user salts; the same one the clients use.</summary>
    private static readonly byte[] LegacySharedSalt = Encoding.UTF8.GetBytes("ShuffullSaltingSixteenBytesLong!");

    /// <summary>Gives <paramref name="user"/> a fresh salt and stores the hash of <paramref name="clientHash"/> under it.</summary>
    public static void Set(User user, string clientHash)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        user.ServerHashSalt = Convert.ToHexString(salt).ToLowerInvariant();
        user.ServerHash = Hash(clientHash, salt);
    }

    public static bool Verify(User user, string clientHash)
    {
        var salt = user.ServerHashSalt == null ? LegacySharedSalt : Convert.FromHexString(user.ServerHashSalt);
        var expected = Hash(clientHash, salt);

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
        Hash(clientHash, new byte[SaltBytes]);
    }

    private static string Hash(string clientHash, byte[] salt)
    {
        var argon2 = new Argon2id(Encoding.UTF8.GetBytes(clientHash))
        {
            MemorySize = MemorySizeKb,
            DegreeOfParallelism = DegreeOfParallelism,
            Iterations = Iterations,
            Salt = salt,
        };
        return Convert.ToHexString(argon2.GetBytes(HashBytes)).ToLowerInvariant();
    }
}
