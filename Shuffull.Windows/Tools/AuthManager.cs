using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Shuffull.Shared;
using Shuffull.Shared.Networking.Models;
using Shuffull.Shared.Networking.Models.Responses;
using Shuffull.Shared.Networking.Models.Server;
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

        public static async Task RefreshAuthentication(string username, string password)
        {
            try
            {
                var userHash = Hasher.Hash($"{username};{password}");
                var response = await ApiRequestManager.UserAuthenticate(username, userHash);
                await LoadSessionData(response.User, response.Token, response.Expiration);
            }
            catch
            {
                throw;
            }
        }

        public static async Task CreateAccount(string username, string password)
        {
            try
            {
                var response = await ApiRequestManager.UserCreate(username, password);
                await LoadSessionData(response.User, response.Token, response.Expiration);
            }
            catch
            {
                throw;
            }
        }

        async public static Task LoadSessionData(User user, string token, DateTime expiration)
        {
            var context = Program.ServiceProvider.GetRequiredService<ShuffullContext>();
            var localSessionData = await context.LocalSessionData.FirstOrDefaultAsync();
            var userExists = await context.Users.Where(x => x.UserId == user.UserId).AnyAsync();
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
                if (user.UserId != localSessionData.UserId)
                {
                    userChanged = true;
                }
            }

            if (userChanged)
            {
                localSessionData.UserId = user.UserId;
                localSessionData.CurrentPlaylistId = -1;
                localSessionData.ActivelyDownload = false;
            }

            // If the user doesn't exist, change the version to ensure they get a full update on sync
            if (!userExists)
            {
                user.Version = DateTime.MinValue;
                context.Users.Add(user);
            }

            localSessionData.Token = token;
            localSessionData.Expiration = expiration;

            if (noDataFound)
            {
                context.LocalSessionData.Add(localSessionData);
            }

            await context.SaveChangesAsync();
        }
    }
}
