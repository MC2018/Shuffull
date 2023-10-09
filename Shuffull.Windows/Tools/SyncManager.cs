using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Shuffull.Windows.Constants;
using Shuffull.Windows.Extensions;
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
using Microsoft.Extensions.DependencyInjection;
using MoreLinq.Extensions;
using LibVLCSharp.Shared;
using System.Net.Http.Headers;

namespace Shuffull.Windows.Tools
{
    public static class SyncManager
    {
        private static System.Timers.Timer _timer;
        private static bool _isSyncing = false;

        public async static Task Initialize()
        {
            // Add request to sync everything before syncing
            // TODO: add timer
            _timer = new System.Timers.Timer(1000 * 60 * 5);
            _timer.Elapsed += OnTimerElapsed;
            _timer.Start();
            await Sync();
        }

        private async static void OnTimerElapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            await Sync();
        }

        public async static Task SubmitRequest(Request request)
        {
            var context = Program.ServiceProvider.GetRequiredService<ShuffullContext>();
            request.UpdateLocalDb(context);
            context.Requests.Add(request);
            await context.SaveChangesAsync();
        }

        public async static Task<bool> RequestManualSync()
        {
            if (!_isSyncing)
            {
                // TODO: make timer fire off instead of just calling Sync
                // Just try to ensure the method calling this continues and doesn't wait for sync
                await Sync();
                return true;
            }

            return false;
        }

        private async static Task Sync()
        {
            if (_isSyncing)
            {
                return;
            }

            _isSyncing = true;

            // Update playlists
            var context = Program.ServiceProvider.GetRequiredService<ShuffullContext>();
            var updatePlaylistsRequest = new GetPlaylistsRequest()
            {
                Guid = Guid.NewGuid().ToString(),
                TimeRequested = DateTime.UtcNow
            };
            await SubmitRequest(updatePlaylistsRequest);

            var requests = context.GetRequests();
            var requestTypes = requests.Select(x => x.RequestType).Distinct().ToList();

            // Run all requests
            foreach (var requestType in requestTypes)
            {
                var requestsToRun = requests.Where(x => x.RequestType == requestType).ToList();
                var requestSuccessful = await RunRequests(requestsToRun, requestType);

                if (requestSuccessful)
                {
                    context.Requests.RemoveRange(requestsToRun);
                }
            }

            await context.SaveChangesAsync();
            _isSyncing = false;
        }

        private async static Task<bool> RunRequests(List<Request> requests, RequestType requestType)
        {
            var requestSuccessful = false;

            switch (requestType)
            {
                case RequestType.UpdateSongLastPlayed:
                    requestSuccessful = UpdateSongLastPlayed(requests.Cast<UpdateSongLastPlayedRequest>().ToList());
                    break;
                case RequestType.UpdatePlaylists:
                    requestSuccessful = await UpdatePlaylists();
                    break;
            }

            return requestSuccessful;
        }

        private static bool UpdateSongLastPlayed(List<UpdateSongLastPlayedRequest> requests)
        {
            var client = Program.ServiceProvider.GetRequiredService<HttpClient>();
            var parameters = new Dictionary<string, string>()
            {
                { "requestsJson", JsonConvert.SerializeObject(requests) }
            };

            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (!client.TryPost(new Uri($"{SiteInfo.Url}song/UpdateLastPlayed"), parameters, out object _))
            {
                return false;
            }

            return true;
        }

        private async static Task<bool> UpdatePlaylists()
        {
            var client = Program.ServiceProvider.GetRequiredService<HttpClient>();
            var parameters = new Dictionary<string, string>()
            {
                { "userId", "1" }
            };

            if (!client.TryPost(new Uri($"{SiteInfo.Url}playlist/GetAllOverview"), parameters, out List<Playlist> accessiblePlaylists))
            {
                return false;
            }

            // TODO: Will probably need to have this called every X minutes
            var context = Program.ServiceProvider.GetRequiredService<ShuffullContext>();
            var accessiblePlaylistIds = accessiblePlaylists.Select(x => x.PlaylistId).ToArray();
            var localPlaylists = context.GetPlaylists();
            var playlistsToFetch = new List<long>();
            var playlistsToAdd = new List<Playlist>();

            // Remove playlists from local if they are no longer accessible
            foreach (var localPlaylist in localPlaylists)
            {
                if (!accessiblePlaylistIds.Contains(localPlaylist.PlaylistId))
                {
                    context.Playlists.Remove(localPlaylist);
                }
            }

            await context.SaveChangesAsync();

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

            if (playlistsToFetch.Any())
            {
                parameters = new Dictionary<string, string>()
                {
                    { "playlistIds", $"{string.Join(",", playlistsToFetch)}" }
                };

                if (!client.TryPost(new Uri($"{SiteInfo.Url}playlist/GetAll"), parameters, out List<Playlist> updatedPlaylists))
                {
                    return false;
                }

                foreach (var updatedPlaylist in updatedPlaylists)
                {
                    context.UpdatePlaylist(updatedPlaylist);
                }

                await context.SaveChangesAsync();
            }

            return true;
        }
    }
}
