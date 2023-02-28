using LibVLCSharp.Forms.Shared;
using LibVLCSharp.Shared;
using Shuffull.Mobile.Services;
using System;
using System.ComponentModel;
using System.Net.Http;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Shuffull.Mobile.Views
{
    public partial class AboutPage : ContentPage
    {
        private static HttpClient _client = new HttpClient() { BaseAddress = new Uri("http://192.168.0.39:7117/") };

        public AboutPage()
        {
            InitializeComponent();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
        }

        private void btnPlayMusic_Clicked(object sender, EventArgs e)
        {
            //var baseAddress = new Uri("");
           // _client.BaseAddress = baseAddress;
            using (var response = _client.GetAsync("music/getmusic").Result)
            using (var content = response.Content)
            {
                var result = content.ReadAsStringAsync().Result;
                MusicService.Play(result);
            }
        }

        private void btnPlayPause_Clicked(object sender, EventArgs e)
        {
            if (MusicService.IsPlaying)
            {
                MusicService.Pause();
            }
            else
            {
                MusicService.Resume();
            }
        }
    }
}