using LibVLCSharp.Shared;
using Microsoft.EntityFrameworkCore;
using MoreLinq.Extensions;
using Shuffull.Mobile.Constants;
using Shuffull.Mobile.Services;
using Shuffull.Mobile.Tools;
using Shuffull.Mobile.Views;
using Shuffull.Shared;
using Shuffull.Shared.Networking.Models;
using System;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
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

            var fileService = DependencyService.Get<IFileService>();
            var dbPath = Path.Combine(fileService.GetRootPath(), LocalDirectories.Database);
            DataManager.Initialize(dbPath);
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
