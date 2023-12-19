using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shuffull.Site.Tools;
using Shuffull.Site;
using Shuffull.Shared.Networking.Models.Server;
using Shuffull.Site.Tools.Authorization;
using Shuffull.Shared.Networking.Models.Responses;
using Shuffull.Shared.Tools;
using System.Text.Json.Serialization;
using System.Text.Json;
using Newtonsoft.Json.Linq;

namespace Shuffull.Tools.Controllers
{
    public class UserController : Controller
    {
        private readonly IServiceProvider _services;

        public UserController(IServiceProvider services)
        {
            _services = services;
        }

        [HttpPost]
        public async Task<IActionResult> Authenticate(string username, string userHash)
        {
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            var jwtHelper = scope.ServiceProvider.GetRequiredService<JwtHelper>();
            var serverHash = Hasher.Hash(userHash);
            var user = await context.Users
                .AsNoTracking()
                .Where(x => x.Username == username && x.ServerHash == serverHash)
                .FirstOrDefaultAsync();

            if (user == null)
            {
                return BadRequest("Username/password is incorrect");
            }

            var expiration = DateTime.UtcNow.AddDays(30);
            var token = jwtHelper.GenerateJwtToken(user, expiration);
            var mappedUser = ClassMapper.Mapper.Map<User>(user);
            var response = new AuthenticateResponse(mappedUser, token, expiration);

            return Ok(response);
        }

        [HttpPost]
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
            var serverHash = Hasher.Hash(userHash);
            user = new Site.Database.Models.User()
            {
                Username = username,
                ServerHash = serverHash,
                Version = DateTime.UtcNow
            };

            context.Users.Add(user);
            await context.SaveChangesAsync();

            var expiration = DateTime.UtcNow.AddDays(30);
            var token = jwtHelper.GenerateJwtToken(user, expiration);
            var mappedUser = ClassMapper.Mapper.Map<User>(user);
            var response = new AuthenticateResponse(mappedUser, token, expiration);

            return Ok(response);
        }

        [HttpGet]
        public async Task<IActionResult> Test()
        {
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();

            var user1 = new Site.Database.Models.User()
            {
                Username = "one",
                Version = DateTime.UtcNow,
                ServerHash = "one"
            };
            var user2 = new Site.Database.Models.User()
            {
                Username = "one",
                Version = DateTime.UtcNow,
                ServerHash = "one"
            };

            context.Users.Add(user1); context.Users.Add(user2);
            await context.SaveChangesAsync();

            return Ok();
        }
    }
}