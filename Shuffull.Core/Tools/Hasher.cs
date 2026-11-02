using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Shuffull.Core.Tools;

/// <summary>
/// <see cref="Argon2Hash"/> is the client's credential hash (the app computes the same over "username;password"),
/// and <see cref="ShaHash"/> names stored media files. Both outputs are stored, so neither may change; HasherTest pins them.
/// </summary>
public static class Hasher
{
    public static string Argon2Hash(string input)
    {
        var argon2 = new Argon2id(Encoding.UTF8.GetBytes(input))
        {
            MemorySize = (int)Math.Pow(2, 14),
            DegreeOfParallelism = 4,
            Iterations = 5,
            Salt = Encoding.UTF8.GetBytes("ShuffullSaltingSixteenBytesLong!")
        };
        var hash = argon2.GetBytes(16);
        return BitConverter.ToString(hash).Replace("-", "").ToLower();
    }

    public static string ShaHash(byte[] fileBytes)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(fileBytes);
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
    }
}
