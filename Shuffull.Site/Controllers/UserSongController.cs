using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Database = Shuffull.Site.Models.Database;
using Shuffull.Shared.Models;
using Shuffull.Site.Tools;
using Shuffull.Site.Models;
using System.Diagnostics;
using Shuffull.Site;
using Results = Shuffull.Shared.Models;
using System.Collections.Generic;
using Shuffull.Site.Tools.Authorization;
using Shuffull.Shared.Models.Responses;
using System.Text.Json.Serialization;
using System.Text.Json;
using Shuffull.Shared.Models.Server;
using Shuffull.Shared.Models.Requests;
using User = Shuffull.Site.Models.Database.User;
using UserSong = Shuffull.Site.Models.Database.UserSong;

namespace Shuffull.Tools.Controllers
{
    public class UserSongController : Controller
    {
        private readonly int MAX_PAGE_LENGTH = 500;
        private readonly IServiceProvider _services;

        public UserSongController(IServiceProvider services)
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

        [HttpPut]
        [Authorize]
        public async Task<IActionResult> CreateMany([FromBody] string[] songIds)
        {
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            var contextUser = HttpContext.Items["User"] as User;
            var songs = await context.Songs
                .AsNoTracking()
                .Where(x => songIds.Contains(x.SongId))
                .ToListAsync();
            var potentialUserSongs = songs.Select(x => new UserSong { SongId = x.SongId, UserId = contextUser.UserId }).ToList();
            var existingSongIds = await context.UserSongs
                .AsNoTracking()
                .Where(x => x.UserId == contextUser.UserId && songIds.Contains(x.SongId))
                .Select(x => x.SongId)
                .ToListAsync();
            var newUserSongs = potentialUserSongs.Where(x => !existingSongIds.Contains(x.SongId)).ToList();

            foreach (var newUserSong in newUserSongs)
            {
                newUserSong.Version = DateTime.UtcNow;
            }

            context.UserSongs.AddRange(newUserSongs);
            await context.SaveChangesAsync();
            return Ok(newUserSongs);
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetAll(DateTime afterDate)
        {
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            var contextUser = HttpContext.Items["User"] as User;
            var userSongs = await context.UserSongs
                .Where(x => x.UserId == contextUser.UserId && x.Version > afterDate)
                .OrderBy(x => x.Version)
                .Take(MAX_PAGE_LENGTH + 1)
                .ToListAsync();
            var endOfList = true;

            if (userSongs.Count > MAX_PAGE_LENGTH)
            {
                endOfList = false;
                userSongs.RemoveAt(MAX_PAGE_LENGTH);
            }

            var response = new PaginatedResponse<Shared.Models.Server.UserSong>()
            {
                Items = ClassMapper.Mapper.Map<List<Shared.Models.Server.UserSong>>(userSongs),
                EndOfList = endOfList
            };

            return Ok(Serializer.Serialize(response));
        }

        /// <summary>
        /// Updates the songs' last played values
        /// </summary>
        /// <param name="requestsJson"></param>
        /// <returns></returns>
        [HttpPost]
        [Authorize]
        public async Task<IActionResult> UpdateLastPlayed([FromBody] List<UpdateSongLastPlayedRequest> requests)
        {
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            var contextUser = HttpContext.Items["User"] as User;

            if (requests == null || !requests.Any())
            {
                return BadRequest("No data was provided.");
            }

            var songIds = requests.Select(x => x.SongId).ToList();
            var dbUserSongs = context.Users
                .Where(x => x.UserId == contextUser.UserId)
                .Select(x => new
                {
                    User = x,
                    UserSongs = x.UserSongs.Where(x => songIds.Contains(x.SongId)).ToList()
                }).First();

            if (!dbUserSongs.UserSongs.Any())
            {
                return BadRequest("No matching data was found.");
            }

            // TODO: make this more efficient?
            foreach (var userSong in dbUserSongs.UserSongs)
            {
                var requestedLastPlayed = requests.Where(x => x.SongId == userSong.SongId).First().LastPlayed;
                
                if (userSong.LastPlayed < requestedLastPlayed)
                {
                    userSong.LastPlayed = requestedLastPlayed;
                    userSong.Version = DateTime.UtcNow;
                }
            }

            dbUserSongs.User.Version = DateTime.UtcNow;
            await context.SaveChangesAsync();
            return Ok();
        }
    }
}