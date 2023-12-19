using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shuffull.Shared.Networking.Models.Server;
using Shuffull.Site.Tools;
using Shuffull.Site.Tools.Authorization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shuffull.Site.Controllers
{
    public class SongController : Controller
    {
        private readonly IServiceProvider _services;

        public SongController(IServiceProvider services)
        {
            _services = services;
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Get(long songId)
        {
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            var song = await context.Songs.Where(x => x.SongId == songId).FirstOrDefaultAsync();

            if (song == null)
            {
                return BadRequest("Song does not exist");
            }

            return Ok(song);
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetAll(int pageIndex)
        {
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            var songs = await context.Songs
                .AsNoTracking()
                .Skip(pageIndex * 50)
                .Take(50)
                .ToListAsync();

            if (!songs.Any())
            {
                return BadRequest("There are not this many songs in the database.");
            }

            var result = ClassMapper.Mapper.Map<List<Song>>(songs);
            return Ok(result);
        }
    }
}
