using LibVLCSharp.Shared;
using Microsoft.EntityFrameworkCore;
using Shuffull.Mobile.Constants;
using Shuffull.Mobile.Services;
using Shuffull.Mobile.Tools;
using Shuffull.Mobile.Views;
using Shuffull.Shared;
using System;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net.Http;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Shuffull.Mobile
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            var client = new HttpClient();

            DependencyService.RegisterSingleton(client);
            Core.Initialize();

            var dbPath = Path.Combine(DependencyService.Get<IFileService>().GetRootPath(), LocalDirectories.Database);
            var context = new ShuffullContext(dbPath);

            if (context.Database.GetPendingMigrations().Any())
            {
                context.Database.Migrate();
            }

            DependencyService.RegisterSingleton(context);
            SyncManager.Initialize();

            MainPage = new AppShell();
            //MainPage = new LoginPage();
            Shell.Current.GoToAsync("//PlaylistSelect").Wait();
        }

        protected override void OnStart()
        {
        }

        protected override void OnSleep()
        {
        }

        protected override void OnResume()
        {
        }
    }
}
