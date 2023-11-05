using Microsoft.IdentityModel.Tokens;
using Shuffull.Site.Configuration;
using Shuffull.Site.Database.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Shuffull.Site.Tools.Authorization
{
    public class JwtHelper
    {
        private readonly JwtConfiguration _configuration;

        public JwtHelper(IServiceProvider services)
        {
            _configuration = services
                .GetRequiredService<IConfiguration>()
                .GetSection(JwtConfiguration.JwtConfigurationSection)
                .Get<JwtConfiguration>();
        }

        public string GenerateJwtToken(User user)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(_configuration.Secret);
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[] { new Claim("UserId", user.UserId.ToString()) }),
                Expires = DateTime.UtcNow.AddDays(30),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }
    }
}
