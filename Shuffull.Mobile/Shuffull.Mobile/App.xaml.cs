using LibVLCSharp.Shared;
using Shuffull.Mobile.Tools;
using Shuffull.Mobile.Views;
using System;
using System.Configuration;
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
            DataManager.Initialize(); // Should come before SyncManager.Initialize
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
