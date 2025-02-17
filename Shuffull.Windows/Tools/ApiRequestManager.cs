﻿using Microsoft.Extensions.DependencyInjection;
using MoreLinq;
using Newtonsoft.Json;
using Shuffull.Shared;
using Shuffull.Shared.Models.Requests;
using Shuffull.Shared.Models.Responses;
using Shuffull.Shared.Models.Server;
using Shuffull.Shared.Tools;
using Shuffull.Windows.Constants;
using Shuffull.Windows.Extensions;
using SQLitePCL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection.Metadata;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;

namespace Shuffull.Windows.Tools
{
    public static class ApiRequestManager
    {
        private static HttpClient GetAuthorizedClient()
        {
            var client = Program.ServiceProvider.GetRequiredService<HttpClient>();
            using var context = Program.ServiceProvider.GetRequiredService<ShuffullContext>();
            var localSessionDate = context.LocalSessionData.First();

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", localSessionDate.Token);

            return client;
        }

        #region Playlist
        public static async Task<List<Playlist>> PlaylistGetAll()
        {
            var client = GetAuthorizedClient();

            try
            {
                var response = await client.GetAsync(new Uri($"{SiteInfo.Url}/playlist/getall"));
                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadFromJsonAsync<List<Playlist>>();
                return result;
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new HttpRequestException("Issue with sending a request to the server", ex);
            }
        }

        public static async Task<List<Playlist>> PlaylistGetList(long[] playlistIds)
        {
            var client = GetAuthorizedClient();
            var playlistIdsJson = $"[{string.Join(",", playlistIds)}]";
            var parameters = new StringContent(playlistIdsJson, Encoding.UTF8, "application/json");

            try
            {
                var response = await client.PostAsync(new Uri($"{SiteInfo.Url}/playlist/getlist"), parameters);
                response.EnsureSuccessStatusCode();

                using var content = response.Content;
                var resultStr = await content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<List<Playlist>>(resultStr);
                return result;
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new HttpRequestException("Issue with sending a request to the server", ex);
            }
        }
        #endregion

        #region User
        public static async Task<AuthenticateResponse> UserAuthenticate(string username, string userHash)
        {
            var client = Program.ServiceProvider.GetRequiredService<HttpClient>();
            var parameters = new Dictionary<string, string>()
            {
                { "username", username },
                { "userHash", userHash }
            };

            try
            {
                var content = await client.PostAsync<AuthenticateResponse>(new Uri($"{SiteInfo.Url}/user/authenticate"), parameters);
                return content;
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new HttpRequestException("Issue with sending a request to the server", ex);
            }
        }

        public static async Task<AuthenticateResponse> UserCreate(string username, string password)
        {
            var client = Program.ServiceProvider.GetRequiredService<HttpClient>();
            var userHash = Hasher.Argon2Hash($"{username};{password}");
            var parameters = new Dictionary<string, string>()
            {
                { "username", username },
                { "userHash", userHash }
            };

            try
            {
                var content = await client.PostAsync<AuthenticateResponse>(new Uri($"{SiteInfo.Url}/user/create"), parameters);
                return content;
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new HttpRequestException("Issue with sending a request to the server", ex);
            }
        }

        public static async Task<User> UserGet()
        {
            var client = GetAuthorizedClient();

            try
            {
                var response = await client.GetAsync(new Uri($"{SiteInfo.Url}/user/get"));
                response.EnsureSuccessStatusCode();

                //var result = await response.Content.ReadFromJsonAsync<User>();
                var resultStr = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<User>(resultStr);
                return result;
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new HttpRequestException("Issue with sending a request to the server", ex);
            }
        }
        #endregion

        #region UserSong
        /// <summary>
        /// Sends a request to update the last played timestamp of songs to the server
        /// </summary>
        /// <param name="requests">List of requests</param>
        /// <returns></returns>
        /// <exception cref="HttpRequestException"></exception>
        public static async Task UserSongUpdateLastPlayed(List<UpdateSongLastPlayedRequest> requests)
        {
            var client = GetAuthorizedClient();
            var requestsJson = JsonConvert.SerializeObject(requests);
            var parameters = new StringContent(requestsJson, Encoding.UTF8, "application/json");

            try
            {
                using var response = await client.PostAsync($"{SiteInfo.Url}/usersong/updatelastplayed", parameters);
                response.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new HttpRequestException("Issue with sending a request to the server", ex);
            }
        }

        public static async Task UserSongCreateMany(List<long> songIds)
        {
            var client = GetAuthorizedClient();
            var songIdsJson = JsonConvert.SerializeObject(songIds);
            var parameters = new StringContent(songIdsJson, Encoding.UTF8, "application/json");

            try
            {
                using var response = await client.PutAsync($"{SiteInfo.Url}/usersong/createmany", parameters);
                response.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new HttpRequestException("Issue with sending a request to the server", ex);
            }
        }

        public static async Task<PaginatedResponse<UserSong>> UserSongGetAll(DateTime afterDate)
        {
            var client = GetAuthorizedClient();
            var parameters = $"afterDate={afterDate:yyyy-MM-ddTHH:mm:ss.fffffff}";

            try
            {
                using var response = await client.GetAsync($"{SiteInfo.Url}/usersong/getall?{parameters}");
                response.EnsureSuccessStatusCode();

                using var content = response.Content;
                var resultStr = await content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<PaginatedResponse<UserSong>>(resultStr);
                return result;
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new HttpRequestException("Issue with sending a request to the server", ex);
            }
        }
        #endregion

        #region Song
        public static async Task<List<Song>> SongGetList(long[] songIds)
        {
            var client = GetAuthorizedClient();
            var songIdsJson = $"[{string.Join(",", songIds)}]";
            var parameters = new StringContent(songIdsJson, Encoding.UTF8, "application/json");

            try
            {
                using var response = await client.PostAsync($"{SiteInfo.Url}/song/getlist", parameters);
                response.EnsureSuccessStatusCode();

                using var content = response.Content;
                var resultStr = await content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<List<Song>>(resultStr);
                return result;
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new HttpRequestException("Issue with sending a request to the server", ex);
            }
        }
        #endregion

        #region Tag
        public static async Task<List<Tag>> TagGetAll()
        {
            var client = GetAuthorizedClient();

            try
            {
                using var response = await client.GetAsync($"{SiteInfo.Url}/tag/getall");
                response.EnsureSuccessStatusCode();

                using var content = response.Content;
                var resultStr = await content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<List<Tag>>(resultStr);
                return result;
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new HttpRequestException("Issue with sending a request to the server", ex);
            }
        }
        #endregion
    }
}
