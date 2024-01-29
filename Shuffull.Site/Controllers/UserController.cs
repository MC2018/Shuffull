using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shuffull.Site.Tools;
using Shuffull.Site;
using Shuffull.Site.Tools.Authorization;
using Shuffull.Shared.Models.Responses;
using Shuffull.Shared.Tools;
using System.Text.Json.Serialization;
using System.Text.Json;
using Newtonsoft.Json.Linq;
using Shuffull.Shared.Models.Server;

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
            user = new Site.Models.Database.User()
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
        [Authorize]
        public async Task<IActionResult> Get()
        {
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            var contextUser = HttpContext.Items["User"] as Site.Models.Database.User;
            var user = await context.Users
                .AsNoTracking()
                .Where(x => x.UserId == contextUser.UserId)
                .FirstAsync();
            var result = ClassMapper.Mapper.Map<User>(user);
            var options = new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.Preserve
            };
            var resultStr = JsonSerializer.Serialize(result, options);

            return Ok(resultStr);
        }
    }
}