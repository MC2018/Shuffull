using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Shuffull.Windows.Constants;
using Shuffull.Windows.Extensions;
using Shuffull.Shared;
using Shuffull.Shared.Enums;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using LibVLCSharp.Shared;
using System.Net.Http.Headers;
using System.Net;
using Shuffull.Shared.Models.Server;
using Shuffull.Shared.Models.Requests;

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
            using var context = Program.ServiceProvider.GetRequiredService<ShuffullContext>();
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
                using var context = Program.ServiceProvider.GetRequiredService<ShuffullContext>();

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

                var endedPrematurely = false;

                // Run all requests
                // Pushing changes
                foreach (var requestBatch in requestBatches)
                {
                    var statusCode = await RunRequests(requestBatch);
                    var statusCodeInt = (int)statusCode;

                    if (statusCode == HttpStatusCode.Unauthorized)
                    {
                        // TODO: figure out a way for it to auto-log you out, pause any playing music
                        await AuthManager.ClearAuthentication();
                        endedPrematurely = true;
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
                        endedPrematurely = true;
                        break;
                    }
                }

                if (endedPrematurely)
                {
                    return;
                }

                // Pulling changes
                var overallSyncRequest = new OverallSyncRequest()
                {
                    Guid = Guid.NewGuid().ToString(),
                    TimeRequested = DateTime.UtcNow
                };
                var list = new List<Request>() { overallSyncRequest };
                await RunRequests(list);
            }
            catch (Exception ex)
            {

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
        /// <param name="requests">A collection of requests</param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        private async static Task<HttpStatusCode> RunRequests(ICollection<Request> requests)
        {
            var singleRequest = requests.First();
            var requestType = singleRequest.RequestType;
            HttpStatusCode statusCode;

            switch (requestType)
            {
                case RequestType.UpdateSongLastPlayed:
                    statusCode = await UpdateSongLastPlayed(requests.Cast<UpdateSongLastPlayedRequest>().ToList());
                    break;
                case RequestType.OverallSync:
                    statusCode = await OverallSync();
                    break;
                case RequestType.CreateUserSong:
                    statusCode = await CreateUserSong(requests.Cast<CreateUserSongRequest>().ToList());
                    break;
                default:
                    throw new Exception("A request type has no method to call.");
            }

            return statusCode;
        }

        private static async Task<HttpStatusCode> CreateUserSong(List<CreateUserSongRequest> requests)
        {
            try
            {
                var songIds = requests.Select(x => x.SongId).ToList();
                await ApiRequestManager.UserSongCreateMany(songIds);
                return HttpStatusCode.OK;
            }
            catch (HttpRequestException ex)
            {
                return ex.StatusCode ?? HttpStatusCode.InternalServerError;
            }
        }

        private static async Task<HttpStatusCode> OverallSync()
        {
            try
            {
                using var context = Program.ServiceProvider.GetRequiredService<ShuffullContext>();
                using var transaction = context.Database.BeginTransaction();
                var localSessionData = context.LocalSessionData.First();
                var localUser = context.Users.Where(x => x.UserId == localSessionData.UserId).First();

                // Update user version
                var newUser = await ApiRequestManager.UserGet();

                //if (newUser.Version == localUser.Version)
                //{
                //    return HttpStatusCode.OK;
                //}

                context.Users.Remove(localUser);
                context.Users.Add(newUser);

                // Refresh tags
                var tags = await ApiRequestManager.TagGetAll();
                context.UpdateTags(tags);
                await context.SaveChangesAsync();

                // Refresh playlists
                var parameters = new Dictionary<string, string>()
                {
                    { "userId", $"{localSessionData.UserId}" }
                };
                var accessiblePlaylists = await ApiRequestManager.PlaylistGetAll();
                var accessiblePlaylistIds = accessiblePlaylists.Select(x => x.PlaylistId).ToArray();
                var localPlaylists = context.GetPlaylists();
                var playlistsToFetch = new List<long>();
                var playlistsToAdd = new List<Playlist>();
                var updatedPlaylists = new List<Playlist>();

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
                    // TODO: remove GetList in the future and just make this a loop of PlaylistGet?
                    updatedPlaylists = await ApiRequestManager.PlaylistGetList(playlistsToFetch.ToArray());

                    foreach (var updatedPlaylist in updatedPlaylists)
                    {
                        await context.UpdatePlaylist(updatedPlaylist);
                    }
                }

                // Refresh user songs
                // TODO: test to make sure this works w/an updated usersong at the end that was already added to context
                var afterDate = localUser.Version;
                var endOfList = false;
                var updatedUserSongs = new List<UserSong>();

                while (!endOfList)
                {
                    var paginatedResponse = await ApiRequestManager.UserSongGetAll(afterDate);
                    var userSongs = paginatedResponse.Items;
                    await context.UpdateUserSongs(userSongs);
                    updatedUserSongs.AddRange(userSongs);

                    endOfList = paginatedResponse.EndOfList;

                    if (!endOfList)
                    {
                        afterDate = userSongs.Last().Version;
                    }
                }

                // Combine UserSongs+PlaylistSongs and cross-verify which songs aren't on the local device
                // (cross-verify after queueing all)
                var localSongIds = context.Songs.Select(x => x.SongId).ToList();
                var newSongIds = updatedPlaylists
                    .SelectMany(x => x.PlaylistSongs)
                    .Select(x => x.SongId)
                    .Concat(updatedUserSongs.Select(x => x.SongId))
                    .Distinct()
                    .Where(x => !localSongIds.Contains(x))
                    .ToList();
                var existingArtistIds = context.Artists.Select(x => x.ArtistId).ToHashSet();

                // Get cross-verified songs, add to context
                for (int i = 0; i * 500 < newSongIds.Count; i++)
                {
                    var songIdsSubset = newSongIds.Skip(i * 500).Take(500).ToArray();
                    var newSongs = await ApiRequestManager.SongGetList(songIdsSubset);
                    var newSongsCopy = newSongs.ToList(); // TODO: do the same thing for songtags as this; load in all tags separately
                    /*var artists = newSongs
                        .SelectMany(x => x.SongArtists)
                        .Select(x => x.Artist)
                        .DistinctBy(x => x.ArtistId)
                        .Where(x => !existingArtistIds.Contains(x.ArtistId))
                        .ToList();*/
                    context.Songs.AddRange(newSongs);
                    /*foreach (var artist in artists)
                    {
                        artist.SongArtists = null;
                        context.Artists.Add(artist);
                        existingArtistIds.Add(artist.ArtistId);
                    }

                    foreach (var newSongCopy in newSongsCopy) // TODO: do i need this separate addition of artists?
                    {
                        MoreLinq.Extensions.ForEachExtension.ForEach(newSongCopy.SongArtists, x => x.Artist = null);
                    }

                    foreach (var newSongCopy in newSongsCopy) // TODO: do i need this separate addition of artists?
                    {
                        MoreLinq.Extensions.ForEachExtension.ForEach(newSongCopy.SongArtists, x => x.Artist = null);
                    }

                    context.Songs.AddRange(newSongsCopy);*/
                }

                // TODO: Request updated songs based on Version here (song.version should be implemented)
                // requires new data structure to tie all affected users with updated songs

                await context.SaveChangesAsync();
                await transaction.CommitAsync();

                return HttpStatusCode.OK;
            }
            catch (HttpRequestException ex)
            {
                return ex.StatusCode ?? HttpStatusCode.InternalServerError;
            }
            catch (Exception)
            {
                return HttpStatusCode.InternalServerError;
            }
        }

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
    }
}
