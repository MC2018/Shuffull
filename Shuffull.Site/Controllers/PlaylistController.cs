using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shuffull.Site.Database;
using Shuffull.Site.Tools;
using Shuffull.Site.Models;
using System.Diagnostics;
using Shuffull.Site;
using Results = Shuffull.Shared.Networking.Models;
using Shuffull.Shared.Networking.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shuffull.Tools.Controllers
{
    public class PlaylistController : Controller
    {
        private readonly IServiceProvider _services;

        public PlaylistController(IServiceProvider services)
        {
            _services = services;
        }

        public List<Playlist> GetAllOverview(long userId)
        {
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            var playlists = context.Playlists
                .AsNoTracking()
                .Where(x => x.UserId == userId)
                .ToList();
            var result = ClassMapper.Mapper.Map<List<Playlist>>(playlists);

            return result;
        }

        public string GetAll(long[] playlistIds)
        {
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            var playlist = context.Playlists
                .AsNoTracking()
                .Where(x => playlistIds.Contains(x.PlaylistId))
                .Include(x => x.PlaylistSongs)
                .ThenInclude(x => x.Song)
                .ToList();
            var result = ClassMapper.Mapper.Map<List<Playlist>>(playlist);

            var options = new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.Preserve
            };
            var resultStr = JsonSerializer.Serialize(result, options);
            
            return resultStr;
        }

        public string Get(long playlistId)
        {
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            var playlist = context.Playlists
                .AsNoTracking()
                .Where(x => x.PlaylistId == playlistId)
                .Include(x => x.PlaylistSongs)
                .ThenInclude(x => x.Song)
                .FirstOrDefault();
            var result = ClassMapper.Mapper.Map<Playlist>(playlist);

            var options = new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.Preserve
            };

            var resultStr = JsonSerializer.Serialize(result, options);
            return resultStr;
        }
    }
}