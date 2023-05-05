using Microsoft.AspNetCore.Mvc;
using Shuffull.Site.Database;
using Shuffull.Site.Database.Models;
using Shuffull.Site;
using Shuffull.Site.Models;
using Shuffull.Site.Services;
using System.Diagnostics;

namespace Shuffull.Tools.Controllers
{
    public class HomeController : Controller
    {
        private readonly IServiceProvider _services;

        public HomeController(IServiceProvider services)
        {
            _services = services;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }
        public IActionResult Upload()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(100_000_000_000)]
        public async Task<IActionResult> Upload(string username, string playlistName, IEnumerable<IFormFile> files)
        {
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            var songImportService = scope.ServiceProvider.GetRequiredService<SongImportService>();
            var user = context.Users.Where(x => x.Username == username).FirstOrDefault();

            if (user == null)
            {
                return View();
            }

            var playlist = context.Playlists.Where(x => x.Name == playlistName).FirstOrDefault();

            if (playlist == null)
            {
                playlist = new Playlist()
                {
                    UserId = user.UserId,
                    Name = playlistName,
                    CurrentSongId = 0,
                    PercentUntilReplayable = 0.9m
                };

                context.Playlists.Add(playlist);
                await context.SaveChangesAsync();
            }

            songImportService.DownloadAndImportFiles(files, playlist.PlaylistId);


            return RedirectToAction("Index");
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}