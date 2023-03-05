using LibVLCSharp.Forms.Shared;
using LibVLCSharp.Shared;
using Newtonsoft.Json;
using Shuffull.Mobile.Services;
using Shuffull.Shared.Networking.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Shuffull.Mobile.Views
{
    public partial class AboutPage : ContentPage
    {
        private static readonly string _url = "http://192.168.0.39:7117/";
        private static readonly HttpClient _client = new HttpClient() { BaseAddress = new Uri(_url) };

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
            var parameters = new Dictionary<string, string>()
            {
                { "playlistId", "1" }
            };
            var encodedContent = new FormUrlEncodedContent(parameters);

            using (var response = _client.PostAsync("music/GetPlaylist", encodedContent).Result)
            using (var content = response.Content)
            {
                if (response.IsSuccessStatusCode)
                {
                    var resultStr = content.ReadAsStringAsync().Result;
                    var result = JsonConvert.DeserializeObject<PlaylistResult>(resultStr);
                    MusicService.Play($"{_url}music/{result.Songs.First().Directory}");
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