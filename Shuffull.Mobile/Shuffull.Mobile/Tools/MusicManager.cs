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
using Xamarin.Forms;
using static SQLite.SQLite3;

namespace Shuffull.Mobile.Tools
{
    internal class MusicManager
    {
        private static readonly LibVLC _libvlc;
        private static MediaPlayer _mediaPlayer = null;

        public static bool IsPlaying { get { return _mediaPlayer?.IsPlaying ?? false; } }

        static MusicManager()
        {
            _libvlc = new LibVLC();
        }

        public static void PlayNewSong()
        {
            Song song;
            string songUrl;

            try
            {
                _mediaPlayer?.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                _mediaPlayer = null;
            }

            try
            {
                song = DataManager.GetNextSong(); // TODO: Make this method attempt to fetch from server if failure
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
                    PlayNewSong();
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
