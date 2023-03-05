using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shuffull.Database;
using Shuffull.Shared.Networking.Models;
using Shuffull.Site.Tools;
using Shuffull.Site.Models;
using System.Diagnostics;

namespace Shuffull.Tools.Controllers
{
    public class MusicController : Controller
    {
        private IServiceProvider _services;

        public MusicController(IServiceProvider services)
        {
            _services = services;
        }

        public string Index()
        {
            return "Index";
        }

        public PlaylistResult GetPlaylist(long playlistId)
        {
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            var playlist = context.Playlists
                .AsNoTracking()
                .Where(x => x.PlaylistId == playlistId)
                .Select(x => new { Playlist = x, Songs = x.PlaylistSongs.Select(x => x.Song) })
                .FirstOrDefault();
            var result = ClassMapper.Mapper.Map<PlaylistResult>(playlist?.Playlist);
            result.Songs = ClassMapper.Mapper.Map<ICollection<SongResult>>(playlist?.Songs);

            return result;
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}