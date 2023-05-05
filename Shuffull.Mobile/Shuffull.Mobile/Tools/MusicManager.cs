using LibVLCSharp.Shared;
using Shuffull.Mobile.Constants;
using Shuffull.Mobile.Tools;
using Shuffull.Shared.Networking.Models;
using Shuffull.Shared.Networking.Models.Requests;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using Xamarin.Forms;
using static SQLite.SQLite3;

namespace Shuffull.Mobile.Tools
{
    internal class MusicManager
    {
        private static readonly LibVLC _libvlc;
        private static MediaPlayer _mediaPlayer = null;
        private static readonly System.Timers.Timer _timer;
        private static bool _playNewSong = false;

        public static bool IsPlaying { get { return _mediaPlayer?.IsPlaying ?? false; } }

        static MusicManager()
        {
            _libvlc = new LibVLC();
            _timer = new System.Timers.Timer(50);
            _timer.Elapsed += OnTimerElapsed;
            _timer.Start();
        }

        private static void OnTimerElapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (_playNewSong)
            {
                _playNewSong = false;
                PlayNewSong();
            }
        }

        public static void PlayNewSong()
        {
            Song song;
            string songUrl;

            try
            {
                _mediaPlayer?.Dispose();
            }
            catch (ObjectDisposedException e)
            {
            }
            finally
            {
                _mediaPlayer = null;
            }

            try
            {
                song = DataManager.GetNextSong(); // TODO: Make this method attempt to fetch from server if failure?
                songUrl = $"{SiteInfo.Url}music/{song.Directory}";
            }
            catch (InvalidOperationException)
            {
                return;
            }

            using (var media = new Media(_libvlc, new Uri(songUrl)))
            {
                _mediaPlayer = new MediaPlayer(media)
                {
                    EnableHardwareDecoding = true
                };

                _mediaPlayer.Play();
                _mediaPlayer.EndReached += (sender, args) =>
                {
                    _playNewSong = true;
                };
            }

            var request = new UpdateSongLastPlayedRequest()
            {
                LastPlayed = DateTime.UtcNow,
                Guid = Guid.NewGuid().ToString(),
                SongId = song.SongId,
                TimeRequested = DateTime.UtcNow
            };
            DataManager.AddRequest(request);
        }

        public static void Play()
        {
            if (_mediaPlayer == null)
            {
                PlayNewSong();
            }
            else
            {
                _mediaPlayer.Play();
            }
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
