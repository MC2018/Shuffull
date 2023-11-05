using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shuffull.Site.Tools;
using Shuffull.Site;
using Shuffull.Shared.Networking.Models.Server;
using Shuffull.Site.Tools.Authorization;
using Shuffull.Shared.Networking.Models.Responses;
using OpenAI_API.Moderation;

namespace Shuffull.Tools.Controllers
{
    public class UserController : Controller
    {
        private readonly IServiceProvider _services;

        public UserController(IServiceProvider services)
        {
            _services = services;
        }

        public async Task<IActionResult> Authenticate(string username, string userHash)
        {
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            var jwtHelper = scope.ServiceProvider.GetRequiredService<JwtHelper>();
            var passwordHash = await Hasher.Hash(userHash);
            var user = await context.Users
                .Where(x => x.Username == username && x.PasswordHash == passwordHash)
                .FirstOrDefaultAsync();

            if (user == null)
            {
                return BadRequest(new { message = "Username/password is incorrect" });
            }

            var token = jwtHelper.GenerateJwtToken(user);
            var mappedUser = ClassMapper.Mapper.Map<User>(user);
            var response = new AuthenticateResponse(mappedUser, token);

            return Ok(response);
        }

        public async Task<IActionResult> Create(string username, string userHash)
        {
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            var jwtHelper = scope.ServiceProvider.GetRequiredService<JwtHelper>();
            var passwordHash = await Hasher.Hash(userHash);
            var user = new Site.Database.Models.User()
            {
                Username = username,
                PasswordHash = passwordHash
            };

            context.Users.Add(user);
            var response = ClassMapper.Mapper.Map<User>(user);
            return Ok(response);
        }
    }
}