using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Shuffull.Shared;
using Shuffull.Shared.Networking.Models.Requests;
using Shuffull.Windows.Constants;
using Shuffull.Windows.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;

namespace Shuffull.Windows.Tools
{
    public static class ApiRequestManager
    {
        private static HttpClient GetAuthorizedClient()
        {
            var client = Program.ServiceProvider.GetRequiredService<HttpClient>();
            var context = Program.ServiceProvider.GetRequiredService<ShuffullContext>();
            var localSessionDate = context.LocalSessionData.First();

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", localSessionDate.Token);

            return client;
        }

        /// <summary>
        /// Sends a request to update the last played timestamp of songs to the server
        /// </summary>
        /// <param name="requests">List of requests</param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="HttpRequestException"></exception>
        /// <exception cref="TaskCanceledException"></exception>
        public static async Task UpdateSongLastPlayed(List<UpdateSongLastPlayedRequest> requests)
        {
            var client = GetAuthorizedClient();
            var parameters = new Dictionary<string, string>()
            {
                { "requestsJson", JsonConvert.SerializeObject(requests) }
            };
            var encodedRequest = new FormUrlEncodedContent(parameters);

            try
            {
                using var response = await client.PostAsync($"{SiteInfo.Url}song/UpdateLastPlayed", encodedRequest);
                var content = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    return;
                }
                else
                {
                    throw new HttpRequestException(content, null, response.StatusCode);
                }
            }
            catch
            {
                throw;
            }
        }
    }
}
