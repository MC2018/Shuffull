using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Shuffull.Mobile.Constants;
using Shuffull.Mobile.Extensions;
using Shuffull.Shared;
using Shuffull.Shared.Enums;
using Shuffull.Shared.Networking.Models;
using Shuffull.Shared.Networking.Models.Requests;
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
            // Add request to sync everything before syncing
            // TODO: add timer
            Sync();
        }

        public static void RequestManualSync()
        {
            if (!_isSyncing)
            {
                // TODO: make timer fire off instead of just calling Sync
                // Just try to ensure the method calling this continues and doesn't wait for sync
                Sync();
            }
        }

        private static void Sync()
        {
            if (_isSyncing)
            {
                return;
            }

            _isSyncing = true;

            // Update playlists
            var updatePlaylistsRequest = new UpdatePlaylistsRequest()
            {
                Guid = Guid.NewGuid().ToString(),
                TimeRequested = DateTime.UtcNow
            };

            DataManager.AddRequest(updatePlaylistsRequest);

            var requests = DataManager.GetRequests();
            var requestTypes = requests.Select(x => x.RequestType).Distinct().ToList();

            // Run all requests
            foreach (var requestType in requestTypes)
            {
                var requestsToRun = requests.Where(x => x.RequestType == requestType).ToList();
                var requestSuccessful = RunRequests(requestsToRun, requestType);

                if (requestSuccessful)
                {
                    foreach (var requestToRun in requestsToRun)
                    {
                        DataManager.RemoveRequest(requestToRun);
                    }
                }
            }

            _isSyncing = false;
        }

        private static bool RunRequests(List<Request> requests, RequestType requestType)
        {
            var requestSuccessful = false;

            switch (requestType)
            {
                case RequestType.UpdateSongLastPlayed:
                    requestSuccessful = UpdateSongLastPlayed(requests.Cast<UpdateSongLastPlayedRequest>().ToList());
                    break;
                case RequestType.UpdatePlaylists:
                    requestSuccessful = UpdatePlaylists();
                    break;
            }

            return requestSuccessful;
        }

        private static bool UpdateSongLastPlayed(List<UpdateSongLastPlayedRequest> requests)
        {
            var client = DependencyService.Get<HttpClient>();
            var parameters = new Dictionary<string, string>()
            {
                { "requests", JsonConvert.SerializeObject(requests) }
            };

            if (!client.TryPost(new Uri($"{SiteInfo.Url}song/UpdateLastPlayed"), parameters, out object _))
            {
                return false;
            }

            DataManager.IncrementPlaylistsBySongIds(requests.Select(x => x.SongId).ToList());

            return true;
        }

        private static bool UpdatePlaylists()
        {
            var client = DependencyService.Get<HttpClient>();
            var parameters = new Dictionary<string, string>()
            {
                { "userId", "1" }
            };

            if (!client.TryPost(new Uri($"{SiteInfo.Url}playlist/GetAllOverview"), parameters, out List<Playlist> accessiblePlaylists))
            {
                return false;
            }

            // TODO: Will probably need to have this called every X minutes
            var accessiblePlaylistIds = accessiblePlaylists.Select(x => x.PlaylistId).ToArray();
            var localPlaylists = DataManager.GetPlaylists();
            var playlistsToFetch = new List<long>();
            var playlistsToAdd = new List<Playlist>();

            // Remove playlists from local if they are no longer accessible
            foreach (var localPlaylist in localPlaylists)
            {
                if (!accessiblePlaylistIds.Contains(localPlaylist.PlaylistId))
                {
                    DataManager.RemovePlaylist(localPlaylist.PlaylistId);
                }
            }

            // Create a list of playlists that need updating
            foreach (var accessiblePlaylist in accessiblePlaylists)
            {
                var localPlaylist = localPlaylists.Where(x => x.PlaylistId == accessiblePlaylist.PlaylistId).FirstOrDefault();

                if (localPlaylist == null || localPlaylist.VersionCounter < accessiblePlaylist.VersionCounter)
                {
                    playlistsToFetch.Add(accessiblePlaylist.PlaylistId);
                }
                else if (localPlaylist.VersionCounter > accessiblePlaylist.VersionCounter)
                {
                    // TODO: POST new info to server; could just be that something gets added to the request pile
                }
            }

            parameters = new Dictionary<string, string>()
            {
                { "playlistIds", $"{string.Join(",", playlistsToFetch)}" }
            };



            if (playlistsToFetch.Any())
            {
                if (!client.TryPost(new Uri($"{SiteInfo.Url}playlist/GetAll"), parameters, out List<Playlist> updatedPlaylists))
                {
                    return false;
                }

                foreach (var updatedPlaylist in updatedPlaylists)
                {
                    DataManager.UpdatePlaylist(updatedPlaylist);
                }
            }

            return true;
        }
    }
}
