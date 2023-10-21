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

            if (requests == null)
            {
                return;
            }

            var songIds = requests.Select(x => x.SongId).ToList();
            var songs = context.Songs
                .Where(x => songIds.Contains(x.SongId))
                .Include(x => x.PlaylistSongs)
                .ThenInclude(x => x.Playlist)
                .ToList();
            var playlistIds = songs
                .SelectMany(x => x.PlaylistSongs)
                .Select(x => x.PlaylistId)
                .Distinct()
                .ToList();
            var playlists = context.Playlists
                .Where(x => playlistIds.Contains(x.PlaylistId))
                .ToList();

            foreach (var song in songs)
            {
                var updatedTime = requests.Where(x => x.SongId == song.SongId).First().LastPlayed;

                foreach (var playlistSong in song.PlaylistSongs)
                {
                    if (playlistSong.LastPlayed < updatedTime)
                    {
                        playlistSong.LastPlayed = updatedTime;
                        playlistSong.InQueue = false;
                        // TODO: device ID playlistSong.Playlist.LastUpdatedDevice = ...
                    }
                }
            }

            // Updating of the queues
            foreach (var song in songs)
            {
                context.UpdateQueue(song.SongId);
            }


            foreach (var playlist in playlists)
            {
                playlist.VersionCounter++;
            }

            await context.SaveChangesAsync();
        }
    }
}