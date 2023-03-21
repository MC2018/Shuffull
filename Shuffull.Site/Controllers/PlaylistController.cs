using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shuffull.Database;
using Shuffull.Site.Tools;
using Shuffull.Site.Models;
using System.Diagnostics;
using Shuffull.Site;
using Results = Shuffull.Shared.Networking.Models.Results;

namespace Shuffull.Tools.Controllers
{
    public class PlaylistController : Controller
    {
        private readonly IServiceProvider _services;

        public PlaylistController(IServiceProvider services)
        {
            _services = services;
        }

        public List<Results.Playlist> GetAllOverview(long userId)
        {
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            var playlists = context.Playlists
                .AsNoTracking()
                .Where(x => x.UserId == userId)
                .ToList();
            var result = ClassMapper.Mapper.Map<List<Results.Playlist>>(playlists);

            return result;
        }

        public List<Results.Playlist> GetAll(long[] playlistIds)
        {
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            var playlist = context.Playlists
                .AsNoTracking()
                .Where(x => playlistIds.Contains(x.PlaylistId))
                .Include(x => x.PlaylistSongs)
                .ThenInclude(x => x.Song)
                .ToList();
            var result = ClassMapper.Mapper.Map<List<Results.Playlist>>(playlist);

            return result;
        }

        public Results.Playlist Get(long playlistId)
        {
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            var playlist = context.Playlists
                .AsNoTracking()
                .Where(x => x.PlaylistId == playlistId)
                .Include(x => x.PlaylistSongs)
                .ThenInclude(x => x.Song)
                .FirstOrDefault();
            var result = ClassMapper.Mapper.Map<Results.Playlist>(playlist);

            return result;
        }
    }
}