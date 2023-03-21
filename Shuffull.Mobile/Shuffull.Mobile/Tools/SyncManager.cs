using Shuffull.Mobile.Constants;
using Shuffull.Mobile.Extensions;
using Shuffull.Shared.Networking.Models.Requests;
using Shuffull.Shared.Networking.Models.Results;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using Xamarin.Forms;

namespace Shuffull.Mobile.Tools
{
    public static class SyncManager
    {
        private static bool _isSyncing = false;

        public static void Initialize()
        {
            Sync();
        }

        public static void RequestSync()
        {
            if (!_isSyncing)
            {
                Sync();
            }
        }

        public static void Sync()
        {
            if (_isSyncing)
            {
                return;
            }

            _isSyncing = true;

            var requests = DataManager.GetRequests();




            SyncPlaylists();





            _isSyncing = false;
        }

        private static void SyncPlaylists()
        {
            PushPlaylists();
            PullPlaylists();
        }

        private static void PushPlaylists()
        {

        }

        private static void PullPlaylists()
        {
            var client = DependencyService.Get<HttpClient>();
            var parameters = new Dictionary<string, string>()
            {
                { "userId", "1" }
            };

            if (client.TryPost(new Uri($"{SiteInfo.URL}playlist/GetAllOverview"), parameters, out List<Playlist> accessiblePlaylists))
            {
                // TODO: Will probably need to move this all into its own method, and called every X minutes
                var accessiblePlaylistIds = accessiblePlaylists.Select(x => x.PlaylistId).ToArray();
                var localPlaylists = DataManager.GetPlaylists();
                var playlistsToFetch = new List<long>();
                var playlistsToAdd = new List<Playlist>();

                // Remove playlists from local if they are no longer accessible
                foreach (var localPlaylist in localPlaylists)
                {
                    if (!accessiblePlaylistIds.Contains(localPlaylist.PlaylistId))
                    {
                        DataManager.RemovePlaylist(localPlaylist);
                    }
                }

                // Create a list of playlists that need updating
                foreach (var accessiblePlaylist in accessiblePlaylists)
                {
                    var localPlaylist = localPlaylists.Where(x => x.PlaylistId == accessiblePlaylist.PlaylistId).FirstOrDefault();

                    if (localPlaylist == null || localPlaylist.LastUpdated < accessiblePlaylist.LastUpdated)
                    {
                        playlistsToFetch.Add(accessiblePlaylist.PlaylistId);
                    }
                    else if (localPlaylist.LastUpdated > accessiblePlaylist.LastUpdated)
                    {
                        // TODO: POST new info to server
                    }
                }

                parameters = new Dictionary<string, string>()
                {
                    { "playlistIds", $"{string.Join(",", playlistsToFetch)}" }
                };

                if (playlistsToFetch.Any()
                    && client.TryPost(new Uri($"{SiteInfo.URL}playlist/GetAll"), parameters, out List<Playlist> updatedPlaylists))
                {
                    foreach (var updatedPlaylist in updatedPlaylists)
                    {
                        DataManager.AddPlaylist(updatedPlaylist);
                    }
                }

                DataManager.SetCurrentPlaylist(DataManager.GetPlaylists().FirstOrDefault()?.PlaylistId ?? 0);
            }
        }
    }
}
