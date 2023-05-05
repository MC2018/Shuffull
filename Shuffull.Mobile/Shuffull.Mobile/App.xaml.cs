using LibVLCSharp.Shared;
using Microsoft.EntityFrameworkCore;
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

            var dbPath = Path.Combine(DependencyService.Get<IFileService>().GetRootPath(), "data_new.db3");
            var context = new ShuffullContext(dbPath);

            if (context.Database.GetPendingMigrations().Any())
            {
                context.Database.Migrate();
            }

            DataManager.Initialize(context);
            var songs = context.Songs.ToList();
            var fileService = DependencyService.Get<IFileService>();
            DependencyService.RegisterSingleton(context);

            //Database = new Database(Path.Combine(fileService.GetRootPath(), "data.db3"));
            SyncManager.Initialize();

            MainPage = new AppShell();
            //MainPage = new LoginPage();
            Shell.Current.GoToAsync("//AboutPage").Wait();
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
