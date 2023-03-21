using Newtonsoft.Json;
using Shuffull.Mobile.Constants;
using Shuffull.Mobile.Services;
using Shuffull.Shared.Networking.Models;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Forms;

namespace Shuffull.Mobile.Extensions
{
    public static class HttpClientExtensions
    {
        public static bool TryPost<T>(this HttpClient client, Uri uri, Dictionary<string, string> args, out T value)
        {
            var encodedContent = new FormUrlEncodedContent(args);

            try
            {
                using (var response = client.PostAsync(uri, encodedContent).Result)
                using (var content = response.Content)
                {
                    if (response.IsSuccessStatusCode)
                    {
                        var resultStr = content.ReadAsStringAsync().Result;
                        value = JsonConvert.DeserializeObject<T>(resultStr);
                    }
                    else
                    {
                        value = default;
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                value = default;
                return false;
            }
        }
    }
}
