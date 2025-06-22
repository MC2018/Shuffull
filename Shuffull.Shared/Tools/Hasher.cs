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
            using (var sha256 = SHA256.Create())
            {
                //var fileBytes = File.ReadAllBytes(filePath);
                var hashBytes = sha256.ComputeHash(fileBytes);
                var hexStr = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
                return hexStr;
            }
        }
    }
}
