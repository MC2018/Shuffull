using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Shuffull.Windows.Extensions
{
    public static class HttpClientExtensions
    {
        /// <summary>
        /// Runs a GET with arguments and returns a generic object
        /// </summary>
        /// <typeparam name="T">Generic object to return</typeparam>
        /// <param name="client">Current HttpClient</param>
        /// <param name="uri">URI</param>
        /// <returns>Generic object</returns>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="HttpRequestException"></exception>
        /// <exception cref="TaskCanceledException"></exception>
        /// <exception cref="NullReferenceException">Thrown if the object returned is null</exception>
        public static async Task<T> GetAsync<T>(this HttpClient client, Uri uri)
        {
            try
            {
                using var response = await client.GetAsync(uri);
                using var content = response.Content;

                if (response.IsSuccessStatusCode)
                {
                    var resultStr = await content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<T>(resultStr);

                    if (result != null)
                    {
                        return result;
                    }
                    else
                    {
                        throw new NullReferenceException("The result was null");
                    }
                }
                else
                {
                    throw new HttpRequestException(response.Content.ReadAsStringAsync().Result, null, response.StatusCode);
                }
            }
            catch
            {
                throw;
            }
        }

        /// <summary>
        /// Runs a POST with arguments and returns a generic object
        /// </summary>
        /// <typeparam name="T">Generic object to return</typeparam>
        /// <param name="client">Current HttpClient</param>
        /// <param name="uri">URI</param>
        /// <param name="args">POST arguments</param>
        /// <returns>Generic object</returns>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="HttpRequestException"></exception>
        /// <exception cref="TaskCanceledException"></exception>
        /// <exception cref="NullReferenceException">Thrown if the object returned is null</exception>
        public static async Task<T> PostAsync<T>(this HttpClient client, Uri uri, Dictionary<string, string>? args = null)
        {
            if (args == null)
            {
                args = new Dictionary<string, string>();
            }

            var encodedContent = new FormUrlEncodedContent(args);

            try
            {
                using var response = await client.PostAsync(uri, encodedContent);
                response.EnsureSuccessStatusCode();

                using var content = response.Content;
                var resultStr = await content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<T>(resultStr);

                if (result != null)
                {
                    return result;
                }
                else
                {
                    throw new NullReferenceException("The result was null");
                }
            }
            catch
            {
                throw;
            }
        }

        /// <summary>
        /// Runs a POST with arguments
        /// </summary>
        /// <param name="client">Current HttpClient</param>
        /// <param name="uri">URI</param>
        /// <param name="args">POST arguments</param>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="HttpRequestException"></exception>
        /// <exception cref="TaskCanceledException"></exception>
        public static async Task PostAsync(this HttpClient client, Uri uri, Dictionary<string, string> args)
        {
            var encodedContent = new FormUrlEncodedContent(args);

            try
            {
                using var response = await client.PostAsync(uri, encodedContent);
                response.EnsureSuccessStatusCode();
            }
            catch
            {
                throw;
            }
        }
    }
}
