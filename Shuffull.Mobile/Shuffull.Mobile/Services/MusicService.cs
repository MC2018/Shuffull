using LibVLCSharp.Shared;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Shuffull.Mobile.Services
{
    internal class MusicService
    {
        private static readonly LibVLC _libvlc;
        private static MediaPlayer _mediaPlayer = null;

        public static bool IsPlaying { get { return _mediaPlayer?.IsPlaying ?? false; } }

        static MusicService()
        {
            Core.Initialize();
            _libvlc = new LibVLC();
        }

        public static void Test(object sender, EventArgs e)
        {
            Debug.WriteLine("Waaaaaaaaaa");
        }

        public static void Play(string url)
        {
            var media = new Media(_libvlc, new Uri(url));

            /*media.MetaChanged += (sender, args) => {
                var artwork = media.Meta(MetadataType.ArtworkURL);

                if (artwork != null)
                {
                    // Do stuff
                }
            };*/

            if (_mediaPlayer != null)
            {
                _mediaPlayer.Dispose();
            }

            _mediaPlayer = new MediaPlayer(media)
            {
                EnableHardwareDecoding = true
            };

            _mediaPlayer.Play();
            _mediaPlayer.EndReached += Test;
        }

        public static void Resume()
        {
            if (_mediaPlayer == null)
            {
                return;
            }

            _mediaPlayer.Play();
        }

        public static void Pause()
        {
            if (_mediaPlayer == null)
            {
                return;
            }

            _mediaPlayer.Pause();
        }
    }
}
