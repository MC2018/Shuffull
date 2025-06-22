using Microsoft.AspNetCore.Mvc;
using Shuffull.Site.Models.Database;
using Shuffull.Site;
using System.Diagnostics;
using Shuffull.Site.Tools;
using Nut.Results;

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
            var songImportService = scope.ServiceProvider.GetRequiredService<SongImporter>();
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
                    PlaylistId = Ulid.NewUlid().ToString(),
                    UserId = user.UserId,
                    Name = playlistName,
                    CurrentSongId = null,
                    PercentUntilReplayable = 0.9m
                };

                context.Playlists.Add(playlist);
                await context.SaveChangesAsync();
            }

            var uploadFileResult = await songImportService.DownloadAndImportFilesAsync(files, playlist.PlaylistId).ThrowIfError();

            return RedirectToAction("Index");
        }
    }
}