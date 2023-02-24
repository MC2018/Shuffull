using LibVLCSharp.Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace Shuffull.Mobile.Services
{
    internal class MusicService
    {
        private static LibVLC libvlc;
        private static MediaPlayer mediaPlayer = null;

        public static bool IsPlaying { get { return mediaPlayer?.IsPlaying ?? false; } }

        static MusicService()
        {
            Core.Initialize();
            libvlc = new LibVLC();
        }

        public static void Test(object sender, EventArgs e)
        {
        }

        public static void Play(string url)
        {
            var media = new Media(libvlc, new Uri(url));

            if (mediaPlayer != null)
            {
                mediaPlayer.Dispose();
            }

            mediaPlayer = new MediaPlayer(media)
            {
                EnableHardwareDecoding = true
            };

            mediaPlayer.Play();
            mediaPlayer.EndReached += Test;

        }

        public static void Resume()
        {
            if (mediaPlayer == null)
            {
                return;
            }

            mediaPlayer.Play();
        }

        public static void Pause()
        {
            if (mediaPlayer == null)
            {
                return;
            }

            mediaPlayer.Pause();
        }
    }
}
