using LibVLCSharp.Forms.Shared;
using LibVLCSharp.Shared;
using Shuffull.Mobile.Services;
using System;
using System.ComponentModel;
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

        private void btnPlayMusic_Clicked(object sender, EventArgs e)
        {
            MusicService.Play("http://192.168.0.39:7117/music/rain.mp3");
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