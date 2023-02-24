using Microsoft.AspNetCore.Mvc;
using Shuffull.Site.Models;
using System.Diagnostics;

namespace Shuffull.Site.Controllers
{
    public class MusicController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public MusicController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        public string Index()
        {
            return "Index";
        }

        public string GetMusic(string name)
        {
            Path.Combine();
            return Path.Combine(HttpContext.Request.Scheme + "://", HttpContext.Request.Host.ToString(), "music", "rain.mp3");
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}