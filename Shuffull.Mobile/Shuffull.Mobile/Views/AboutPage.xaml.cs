using LibVLCSharp.Forms.Shared;
using LibVLCSharp.Shared;
using Newtonsoft.Json;
using Shuffull.Mobile.Services;
using Shuffull.Shared.Networking.Models;
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
            using (var response = _client.GetAsync("music/getmusic").Result)
            using (var content = response.Content)
            {
                if (response.IsSuccessStatusCode)
                {
                    var resultStr = content.ReadAsStringAsync().Result;
                    var result = JsonConvert.DeserializeObject<SongResult>(resultStr);
                    MusicService.Play(result.Url);
                }
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