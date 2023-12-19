using Konscious.Security.Cryptography;
using System;
using System.Text;
using System.Threading.Tasks;

namespace Shuffull.Shared.Tools
{
    public class Hasher
    {
        public static string Hash(string input)
        {
            var argon2 = new Argon2id(Encoding.UTF8.GetBytes(input))
            {
                MemorySize = (int)Math.Pow(2, 14),
                DegreeOfParallelism = 4,
                Iterations = 5,
            };
            var hash = argon2.GetBytes(16);
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }
    }
}
