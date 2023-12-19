using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Shuffull.Windows.Constants;
using Shuffull.Windows.Extensions;
using Shuffull.Shared;
using Shuffull.Shared.Enums;
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
using Shuffull.Shared.Networking.Models.Server;
using System.Net;

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

            try
            {
                // Update playlists
                var context = Program.ServiceProvider.GetRequiredService<ShuffullContext>();
                var updatePlaylistsRequest = new GetPlaylistsRequest()
                {
                    Guid = Guid.NewGuid().ToString(),
                    TimeRequested = DateTime.UtcNow
                };
                await SubmitRequest(updatePlaylistsRequest);

                var unorderedRequests = context.GetRequests();
                var requestTypes = unorderedRequests.Select(x => x.RequestType).Distinct().ToList();
                var requestBatches = new List<List<Request>>();
                RequestType? lastRequestType = null;
                var onlyOnceRequestsFound = new List<RequestType>();

                foreach (var request in unorderedRequests)
                {
                    if (request.ProcessingMethod == ProcessingMethod.OnlyOnce)
                    {
                        if (onlyOnceRequestsFound.Contains(request.RequestType))
                        {
                            continue;
                        }

                        requestBatches.Add(new List<Request>() { request });
                        onlyOnceRequestsFound.Add(request.RequestType);
                    }
                    else if (request.ProcessingMethod == ProcessingMethod.Individual)
                    {
                        requestBatches.Add(new List<Request>() { request });
                    }
                    else if (request.ProcessingMethod == ProcessingMethod.Batch)
                    {
                        if (request.RequestType != lastRequestType)
                        {
                            requestBatches.Add(new List<Request>() { request });
                        }
                        else
                        {
                            requestBatches.Last().Add(request);
                        }
                    }

                    lastRequestType = request.RequestType;
                }

                /////////////////////////////////// FINISH THE BATCH PROCESSING LOGIC
                // Run all requests
                foreach (var requestBatch in requestBatches)
                {
                    var statusCode = await RunRequests(requestBatch);
                    var statusCodeInt = (int)statusCode;

                    if (statusCode == HttpStatusCode.Unauthorized)
                    {
                        // TODO: figure out a way for it to auto-log you out, pause any playing music
                        await AuthManager.ClearAuthentication();
                        break;
                    }
                    else if (statusCode == HttpStatusCode.Forbidden)
                    {
                        context.Requests.RemoveRange(requestBatch);
                        await context.SaveChangesAsync();
                    }
                    else if (200 <= statusCodeInt && statusCodeInt <= 299)
                    {
                        context.Requests.RemoveRange(requestBatch);
                        await context.SaveChangesAsync();
                    }
                    else if (400 <= statusCodeInt && statusCodeInt <= 499)
                    {
                        context.Requests.RemoveRange(requestBatch);
                        await context.SaveChangesAsync();
                    }
                    else if (500 <= statusCodeInt)
                    {
                        break;
                    }
                }
            }
            finally
            {
                _isSyncing = false;
            }
        }

        /// <summary>
        /// Logic to run a list of requests of the same <see cref="RequestType"/>
        /// There should only be more than one request when <see cref="ProcessingMethod"/> is of type <see cref="ProcessingMethod.Batch"/>
        /// </summary>
        /// <param name="requests">A list of requests</param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        private async static Task<HttpStatusCode> RunRequests(List<Request> requests)
        {
            var singleRequest = requests.First();
            var requestType = singleRequest.RequestType;
            HttpStatusCode? statusCode = null;

            switch (requestType)
            {
                case RequestType.UpdateSongLastPlayed:
                    statusCode = await UpdateSongLastPlayed(requests.Cast<UpdateSongLastPlayedRequest>().ToList());
                    break;
                case RequestType.UpdatePlaylists:
                    statusCode = await RefreshLocalPlaylists();
                    break;
            }

            if (statusCode == null)
            {
                throw new Exception("A request type has no method to call.");
            }

            return statusCode.Value;
        }

        //private static async Task<bool> GetAllSongs()
        //{

        //}

        private static async Task<HttpStatusCode> UpdateSongLastPlayed(List<UpdateSongLastPlayedRequest> requests)
        {
            try
            {
                await ApiRequestManager.UserSongUpdateLastPlayed(requests);
                return HttpStatusCode.OK;
            }
            catch (HttpRequestException ex)
            {
                return ex.StatusCode ?? HttpStatusCode.InternalServerError;
            }
        }

        private async static Task<HttpStatusCode> RefreshLocalPlaylists()
        {
            var context = Program.ServiceProvider.GetRequiredService<ShuffullContext>();
            var localSessionData = context.LocalSessionData.First();
            var parameters = new Dictionary<string, string>()
            {
                { "userId", $"{localSessionData.UserId}" }
            };
            List<Playlist> accessiblePlaylists;

            try
            {
                accessiblePlaylists = await ApiRequestManager.PlaylistGetAll();
            }
            catch (HttpRequestException ex)
            {
                return ex.StatusCode ?? HttpStatusCode.InternalServerError;
            }

            // TODO: Will probably need to have this called every X minutes
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

                if (localPlaylist == null || localPlaylist.Version < accessiblePlaylist.Version)
                {
                    playlistsToFetch.Add(accessiblePlaylist.PlaylistId);
                }
            }

            if (playlistsToFetch.Any())
            {
                try
                {
                    var updatedPlaylists = await ApiRequestManager.PlaylistGetList(playlistsToFetch.ToArray());

                    foreach (var updatedPlaylist in updatedPlaylists)
                    {
                        context.UpdatePlaylist(updatedPlaylist);
                    }

                    context.SaveChanges();
                }
                catch (HttpRequestException ex)
                {
                    return ex.StatusCode ?? HttpStatusCode.InternalServerError;
                }
            }

            return HttpStatusCode.OK;
        }
    }
}
