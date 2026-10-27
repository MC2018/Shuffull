using Konscious.Security.Cryptography;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Shuffull.Shared.Tools
{
    public class Hasher
    {
        /// <summary>
        /// The fixed salt every client uses for its own hash of "username;password", and the salt every server
        /// hash used before per-user salts. It can't be secret: the clients ship it. The client hash puts the
        /// username in the input, which is what keeps two users' hashes apart.
        /// </summary>
        private static readonly byte[] SharedSalt = Encoding.UTF8.GetBytes("ShuffullSaltingSixteenBytesLong!");

        public static string Argon2Hash(string input)
        {
            return Argon2Hash(input, SharedSalt);
        }

        public static string Argon2Hash(string input, byte[] salt)
        {
            var argon2 = new Argon2id(Encoding.UTF8.GetBytes(input))
            {
                MemorySize = (int)Math.Pow(2, 14),
                DegreeOfParallelism = 4,
                Iterations = 5,
                Salt = salt
            };
            var hash = argon2.GetBytes(16);
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }

        public static string ShaHash(byte[] fileBytes)
        {
            using (var sha256 = SHA256.Create())
            {
                var hashBytes = sha256.ComputeHash(fileBytes);
                var hexStr = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
                return hexStr;
            }
        }
    }
}
