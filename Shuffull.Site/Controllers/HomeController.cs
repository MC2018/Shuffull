using Microsoft.AspNetCore.Mvc;
using Shuffull.Core.Models.Database;
using Shuffull.Site;
using System.Diagnostics;
using Nut.Results;
using Shuffull.Site.Services;
using Shuffull.Site.Commands.Songs.UploadSongs;

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
        public async Task<IActionResult> Upload(string username, string? playlistName, IEnumerable<IFormFile> files)
        {
            await new UploadSongHandler(_services).Handle(new UploadSongRequest(username, playlistName, files)).ThrowIfError();

            return RedirectToAction("Index");
        }
    }
}