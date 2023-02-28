using Microsoft.AspNetCore.Mvc;
using Shuffull.Shared.Models;
using Shuffull.Shared;
using Shuffull.Site.Models;
using System.Diagnostics;

namespace Shuffull.Site.Controllers
{
    public class MusicController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private IServiceProvider _services;

        public MusicController(ILogger<HomeController> logger, IServiceProvider services)
        {
            _logger = logger;
            _services = services;
        }

        public string Index()
        {
            return "Index";
        }

        public string GetMusic(string name) 
        {
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            var randomIndex = new Random().Next(0, context.Songs.Count());
            var randomSong = context.Songs.Skip(randomIndex).First();

            return Path.Combine(HttpContext.Request.Scheme + "://", HttpContext.Request.Host.ToString(), "music", randomSong.Directory)
                .Replace("\\", "/");
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}