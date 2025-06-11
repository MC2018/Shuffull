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
using Shuffull.Site.Models.Database;

namespace Shuffull.Tools.Controllers
{
    public class TagController : Controller
    {
        private readonly IServiceProvider _services;

        public TagController(IServiceProvider services)
        {
            _services = services;
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetAll()
        {
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            var tags = await context.Tags.AsNoTracking().ToListAsync();
            var result = ClassMapper.Mapper.Map<List<Shared.Models.Server.Tag>>(tags);
            var options = new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.IgnoreCycles,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };
            var resultStr = JsonSerializer.Serialize(result, options);

            return Ok(result);
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> AddToSong(string tagId, string songId)
        {
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            var tag = await context.Tags.AsNoTracking().Where(x => x.TagId == tagId).FirstAsync();
            var song = await context.Songs
                .AsNoTracking()
                .Where(x => x.SongId == songId)
                .Include(x => x.SongTags)
                .FirstAsync();
            var songTag = new Site.Models.Database.SongTag()
            {
                SongTagId = Ulid.NewUlid().ToString(),
                SongId = songId,
                TagId = tagId
            };

            if (tag == null)
            {
                return BadRequest($"Tag does not exist");
            }
            else if (song == null)
            {
                return BadRequest($"Song does not exist");
            }
            else if (song.SongTags.Where(x => x.TagId == tagId).Any())
            {
                return BadRequest($"Song already contains this tag");
            }

            context.SongTags.Add(songTag);
            await context.SaveChangesAsync();

            var result = ClassMapper.Mapper.Map<Shared.Models.Server.SongTag>(songTag);
            var options = new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.Preserve
            };
            var resultStr = JsonSerializer.Serialize(result, options);

            return Content(resultStr, "application/json");
        }
    }
}