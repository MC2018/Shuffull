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

        public async Task UpdateLastPlayed(List<UpdateSongLastPlayedRequest> requests)
        {
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            var songIds = requests.Select(x => x.SongId).ToList();
            var songs = context.Songs
                .Where(x => songIds.Contains(x.SongId))
                .Include(x => x.PlaylistSongs)
                .ThenInclude(x => x.Playlist)
                .ToList();
            var updatedPlaylists = new List<Database.Models.Playlist>();

            foreach (var song in songs)
            {
                var updatedTime = requests.Where(x => x.SongId == song.SongId).First().LastPlayed;

                foreach (var playlistSong in song.PlaylistSongs)
                {
                    updatedPlaylists.Add(playlistSong.Playlist);

                    if (playlistSong.LastPlayed < updatedTime)
                    {
                        playlistSong.LastPlayed = updatedTime;
                        // TODO: device ID playlistSong.Playlist.LastUpdatedDevice = ...
                    }
                }
            }

            updatedPlaylists = updatedPlaylists.DistinctBy(x => x.PlaylistId).ToList();

            foreach (var updatedPlaylist in updatedPlaylists)
            {
                updatedPlaylist.VersionCounter++;
            }

            await context.SaveChangesAsync();

            //var result = ClassMapper.Mapper.Map<List<Results.Song>>(songs);

            //return result;
            //return null;
        }
    }
}