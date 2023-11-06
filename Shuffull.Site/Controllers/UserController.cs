using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shuffull.Site.Tools;
using Shuffull.Site;
using Shuffull.Shared.Networking.Models.Server;
using Shuffull.Site.Tools.Authorization;
using Shuffull.Shared.Networking.Models.Responses;
using OpenAI_API.Moderation;
using Shuffull.Shared.Tools;

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
            var serverHash = await Hasher.Hash(userHash);
            var user = await context.Users
                .Where(x => x.Username == username && x.ServerHash == serverHash)
                .FirstOrDefaultAsync();

            if (user == null)
            {
                return BadRequest(new { message = "Username/password is incorrect" });
            }

            var expiration = DateTime.UtcNow.AddDays(30);
            var token = jwtHelper.GenerateJwtToken(user, expiration);
            var mappedUser = ClassMapper.Mapper.Map<User>(user);
            var response = new AuthenticateResponse(mappedUser, token, expiration);

            return Ok(response);
        }

        public async Task<IActionResult> Create(string username, string userHash)
        {
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            var user = await context.Users.Where(x => x.Username == username).FirstOrDefaultAsync();

            if (user != null)
            {
                return BadRequest($"User '{username}' already exists");
            }

            var jwtHelper = scope.ServiceProvider.GetRequiredService<JwtHelper>();
            var serverHash = await Hasher.Hash(userHash);
            user = new Site.Database.Models.User()
            {
                Username = username,
                ServerHash = serverHash
            };

            context.Users.Add(user);
            var response = ClassMapper.Mapper.Map<User>(user);
            return Ok(response);
        }
    }
}