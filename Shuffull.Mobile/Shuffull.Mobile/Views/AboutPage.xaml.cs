using LibVLCSharp.Forms.Shared;
using LibVLCSharp.Shared;
using Newtonsoft.Json;
using Shuffull.Mobile.Constants;
using Shuffull.Mobile.Extensions;
using Shuffull.Mobile.Services;
using Shuffull.Mobile.Tools;
using Shuffull.Shared;
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
        public AboutPage()
        {
            InitializeComponent();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
        }

        private void OnLastClicked(object sender, EventArgs e)
        {
            MusicManager.PlayLastSong();
        }

        private void OnNextClicked(object sender, EventArgs e)
        {
            MusicManager.PlayNextSong();
        }

        private void OnPlayPauseClicked(object sender, EventArgs e)
        {
            if (MusicManager.IsPlaying)
            {
                MusicManager.Pause();
            }
            else
            {
                MusicManager.Play();
            }
        }

        private void OnDownloadAllClicked(object sender, EventArgs e)
        {
            if (DataManager.CurrentPlaylistId == 0)
            {
                Shell.Current.DisplayAlert("No Playlist", "No playlist was selected.", "OK");
            }

            var playlist = DataManager.GetPlaylistWithSongs(DataManager.CurrentPlaylistId);
            var songDirectories = playlist.PlaylistSongs.Select(x => x.Song.Directory).ToList();

            foreach (var songDirectory in songDirectories)
            {
                DownloadManager.AddToQueue(songDirectory);
            }
        }
    }
}