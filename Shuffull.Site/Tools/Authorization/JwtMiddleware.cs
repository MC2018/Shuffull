using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Shuffull.Site.Configuration;
using System.IdentityModel.Tokens.Jwt;
using System.Text;

namespace Shuffull.Site.Tools.Authorization
{
    /// <summary>
    /// Runs the middleware for the JSON Web Tokens
    /// </summary>
    public class JwtMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly JwtConfiguration _configuration;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="next">Next delegate</param>
        /// <param name="services">Service provider</param>
        public JwtMiddleware(RequestDelegate next, IServiceProvider services)
        {
            _next = next;
            _configuration = services
                .GetRequiredService<IConfiguration>()
                .GetSection(JwtConfiguration.JwtConfigurationSection)
                .Get<JwtConfiguration>();
        }

        /// <summary>
        /// Checks for an Authorization header on any API call
        /// </summary>
        /// <param name="httpContext">HTTP context</param>
        /// <param name="dbContext">Database context</param>
        /// <returns></returns>
        public async Task Invoke(HttpContext httpContext, ShuffullContext dbContext)
        {
            var token = httpContext.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();

            if (token != null)
            {
                await AttachUserToContext(httpContext, dbContext, token);
            }

            await _next(httpContext);
        }

        /// <summary>
        /// Attaches the user to the context
        /// </summary>
        /// <param name="httpContext">HTTP context</param>
        /// <param name="dbContext">Database context</param>
        /// <param name="token">Token</param>
        /// <returns></returns>
        private async Task AttachUserToContext(HttpContext httpContext, ShuffullContext dbContext, string token)
        {
            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var key = Encoding.ASCII.GetBytes(_configuration.Secret);
                var tokenValidation = await tokenHandler.ValidateTokenAsync(token, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ClockSkew = TimeSpan.Zero
                });

                var jwtToken = (JwtSecurityToken)tokenValidation.SecurityToken;
                var userId = int.Parse(jwtToken.Claims.First(x => x.Type == "UserId").Value);
                httpContext.Items["User"] = await dbContext.Users.Where(x => x.UserId == userId).AsNoTracking().FirstAsync();
            }
            catch
            {

            }
        }
    }
}
