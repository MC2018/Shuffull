using Azure.Core;
using Konscious.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Shuffull.Shared.Networking.Models.Requests;
using Shuffull.Shared.Networking.Models.Responses;
using Shuffull.Site.Database.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Text;

namespace Shuffull.Site.Tools
{
    public class Hasher
    {
        public static async Task<string> Hash(string input)
        {
            var argon2 = new Argon2id(Encoding.UTF8.GetBytes(input))
            {
                MemorySize = (int)Math.Pow(2, 14),
                DegreeOfParallelism = 4,
                Iterations = 5,
            };
            var hash = await argon2.GetBytesAsync(16);
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }
    }
}
