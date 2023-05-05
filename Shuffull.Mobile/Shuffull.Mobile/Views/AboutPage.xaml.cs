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

        private void OnSkipClicked(object sender, EventArgs e)
        {
            MusicManager.PlayNewSong();
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
    }
}