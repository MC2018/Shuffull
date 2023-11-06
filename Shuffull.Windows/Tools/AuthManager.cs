using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Shuffull.Shared;
using Shuffull.Shared.Networking.Models;
using Shuffull.Shared.Networking.Models.Responses;
using Shuffull.Shared.Tools;
using Shuffull.Windows.Constants;
using Shuffull.Windows.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace Shuffull.Windows.Tools
{
    public static class AuthManager
    {
        public static DateTime Expiration
        {
            get
            {
                var context = Program.ServiceProvider.GetRequiredService<ShuffullContext>();
                var localSessionData = context.LocalSessionData.FirstOrDefault();

                if (localSessionData == null)
                {
                    return DateTime.MinValue;
                }

                return localSessionData.Expiration;
            }
        }

        public static bool IsExpired
        {
            get
            {
                var expiration = Expiration;

                if (expiration == DateTime.MinValue)
                {
                    return true;
                }

                return expiration < DateTime.UtcNow;
            }
        }

        async public static Task ClearAuthentication()
        {
            var context = Program.ServiceProvider.GetRequiredService<ShuffullContext>();
            var localSessionData = await context.LocalSessionData.FirstOrDefaultAsync();

            localSessionData.Token = string.Empty;
            localSessionData.Expiration = DateTime.MinValue;
            await context.SaveChangesAsync();
        }

        async public static Task<bool> RefreshAuthentication(string username, string password)
        {
            var client = Program.ServiceProvider.GetRequiredService<HttpClient>();
            var userHash = await Hasher.Hash($"{username};{password}");
            var parameters = new Dictionary<string, string>()
            {
                { "username", username },
                { "userHash", userHash }
            };

            // TODO: get reason for failure and print it to display, maybe just through Exception
            if (client.TryPost(new Uri($"{SiteInfo.Url}user/authenticate"), parameters, out AuthenticateResponse response))
            {
                await LoadSessionData(response);
                return true;
            }
            
            return false;
        }

        async public static Task<bool> CreateAccount(string username, string password)
        {
            var client = Program.ServiceProvider.GetRequiredService<HttpClient>();
            var userHash = await Hasher.Hash($"{username};{password}");
            var parameters = new Dictionary<string, string>()
            {
                { "username", username },
                { "userHash", userHash }
            };

            // TODO: get reason for failure and print it to display, maybe just through Exception
            if (client.TryPost(new Uri($"{SiteInfo.Url}user/Create"), parameters, out AuthenticateResponse response))
            {
                await LoadSessionData(response);
                return true;
            }

            return false;
        }

        async public static Task LoadSessionData(AuthenticateResponse response)
        {
            var context = Program.ServiceProvider.GetRequiredService<ShuffullContext>();
            var localSessionData = await context.LocalSessionData.FirstOrDefaultAsync();
            var userChanged = false;
            var noDataFound = false;

            if (localSessionData == null)
            {
                localSessionData = new LocalSessionData();
                context.LocalSessionData.Add(localSessionData);
                userChanged = noDataFound = true;
            }
            else
            {
                if (response.User.UserId != localSessionData.UserId)
                {
                    userChanged = true;
                }
            }

            if (userChanged)
            {
                localSessionData.UserId = response.User.UserId;
                localSessionData.CurrentPlaylistId = -1;
                localSessionData.ActivelyDownload = false;
            }

            localSessionData.Token = response.Token;
            localSessionData.Expiration = response.Expiration;

            if (noDataFound)
            {
                context.LocalSessionData.Add(localSessionData);
            }

            await context.SaveChangesAsync();
        }
    }
}
