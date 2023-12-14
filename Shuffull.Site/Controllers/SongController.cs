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
using Shuffull.Site.Tools.Authorization;

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

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetAll()
        {
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            var contextUser = HttpContext.Items["User"] as User;
            var songs = await context.UserSongs
                .Where(x => x.UserId == contextUser.UserId)
                .Include(x => x.Song)
                .ThenInclude(x => x.SongTags)
                .ThenInclude(x => x.Tag)
                .Include(x => x.Song)
                .ThenInclude(x => x.SongArtists)
                .ThenInclude(x => x.Artist)
                .ToListAsync();
            
            return Ok(ClassMapper.MapAndSerialize(songs));
        }

        /// <summary>
        /// Updates the songs' last played values (we need a function for PUT)
        /// </summary>
        /// <param name="requestsJson"></param>
        /// <returns></returns>
        [HttpPost]
        [Authorize]
        public async Task<IActionResult> UpdateLastPlayed(string requestsJson)
        {
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            var contextUser = HttpContext.Items["User"] as User;
            var requests = JsonConvert.DeserializeObject<List<UpdateSongLastPlayedRequest>>(requestsJson);
            var newestUpdate = DateTime.MinValue;

            if (requests == null || !requests.Any())
            {
                return BadRequest("No data was provided.");
            }

            var songIds = requests.Select(x => x.SongId).ToList();
            var userSongs = context.Users
                .Where(x => x.UserId == contextUser.UserId)
                .Select(x => new
                {
                    User = x,
                    UserSongs = x.UserSongs.Where(x => songIds.Contains(x.SongId)).ToList()
                }).First();

            if (!userSongs.UserSongs.Any())
            {
                return BadRequest("No matching data was found.");
            }

            // TODO: make this more efficient?
            foreach (var userSong in userSongs.UserSongs)
            {
                var requestedLastPlayed = requests.Where(x => x.SongId == userSong.SongId).First().LastPlayed;
                
                if (userSong.LastPlayed < requestedLastPlayed)
                {
                    userSong.LastPlayed = requestedLastPlayed;

                    if (userSong.LastPlayed > newestUpdate)
                    {
                        newestUpdate = userSong.LastPlayed;
                    }
                }
            }

            userSongs.User.Version = newestUpdate;
            await context.SaveChangesAsync();
            return Ok();
        }
    }
}