using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Database = Shuffull.Site.Database;
using Shuffull.Shared.Networking.Models;
using Shuffull.Site.Tools;
using Shuffull.Site.Models;
using System.Diagnostics;
using Shuffull.Site;
using Results = Shuffull.Shared.Networking.Models;
using Shuffull.Shared.Networking.Models.Requests;
using Shuffull.Site.Database.Models;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Shuffull.Tools.Controllers
{
    public class SongController : Controller
    {
        private readonly IServiceProvider _services;

        public SongController(IServiceProvider services)
        {
            _services = services;
        }


        /*// TODO: This will need to be shared amongst all requests
        private List<IRequest> RemoveDuplicateRequests(List<IRequest> requests, ShuffullContext context)
        {
            var requestGuids = requests.Select(x => x.Guid).ToList();
            var requestLogs = context.RequestLogs.Where(x => requestGuids.Contains(x.Guid)).ToList();

            foreach ()
        }*/

        [HttpPost]
        public async Task UpdateLastPlayed(string requestsJson)
        {
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            var requests = JsonConvert.DeserializeObject<List<UpdateSongLastPlayedRequest>>(requestsJson);
            var userId = 1;

            if (requests == null)
            {
                return;
            }

            var songIds = requests.Select(x => x.SongId).ToList();
            var userSongs = context.Users
                .Where(x => x.UserId == userId)
                .SelectMany(x => x.UserSongs)
                .Where(x => songIds.Contains(x.SongId))
                .ToList();

            foreach (var userSong in userSongs)
            {
                userSong.LastPlayed = requests.Where(x => x.SongId == userSong.SongId).First().LastPlayed;
            }

            await context.SaveChangesAsync();
        }
    }
}