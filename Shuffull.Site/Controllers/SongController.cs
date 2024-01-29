using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shuffull.Shared.Models.Server;
using Shuffull.Site.Tools;
using Shuffull.Site.Tools.Authorization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shuffull.Site.Controllers
{
    public class SongController : Controller
    {
        private readonly IServiceProvider _services;
        private readonly int MAX_PAGE_LENGTH = 500;

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
                .Skip(pageIndex * MAX_PAGE_LENGTH)
                .Take(MAX_PAGE_LENGTH)
                .ToListAsync();

            if (!songs.Any())
            {
                return BadRequest("There are not this many songs in the database.");
            }

            var result = ClassMapper.Mapper.Map<List<Song>>(songs);
            var options = new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.Preserve
            };
            var resultStr = JsonSerializer.Serialize(result, options);
            return Ok(resultStr);
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> GetList([FromBody] long[] songIds)
        {
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            
            if (songIds == null || !songIds.Any())
            {
                return BadRequest("Song IDs were not provided.");
            }
            else if (songIds.Length > MAX_PAGE_LENGTH)
            {
                return BadRequest($"Cannot provide more than {MAX_PAGE_LENGTH} songs at one time.");
            }

            var songs = await context.Songs
                .AsNoTracking()
                .Where(x => songIds.Contains(x.SongId))
                .Include(x => x.SongTags)
                .ThenInclude(x => x.Tag)
                .Include(x => x.SongArtists)
                .ThenInclude(x => x.Artist)
                .ToListAsync();

            var result = ClassMapper.Mapper.Map<List<Song>>(songs);
            var options = new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.Preserve
            };
            var resultStr = JsonSerializer.Serialize(result, options);
            return Ok(resultStr);
        }
    }
}
