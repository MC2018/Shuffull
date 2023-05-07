using LibVLCSharp.Shared;
using Shuffull.Mobile.Constants;
using Shuffull.Mobile.Services;
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
using Xamarin.CommunityToolkit.UI.Views.Options;
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
        private static readonly int _timerCycle = 1000;
        private static int _currentTimerPosition = 0;

        public static bool IsPlaying { get { return _mediaPlayer?.IsPlaying ?? false; } }

        static MusicManager()
        {
            _libvlc = new LibVLC();
            _timer = new System.Timers.Timer(10);
            _timer.Elapsed += OnTimerElapsed;
            _timer.Start();
        }

        private static void OnTimerElapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            // Play new song
            if (_currentTimerPosition % 5 == 0 && _playNewSong)
            {
                _playNewSong = false;
                PlayNewSong();
            }

            if (_currentTimerPosition % 500 == 0 && IsPlaying)
            {
                DataManager.UpdateCurrentlyPlayingSong((int)(_mediaPlayer.Time / 1000));
            }

            if (_currentTimerPosition++ >= _timerCycle)
            {
                _currentTimerPosition = 0;
            }
        }

        public static void PlayNewSong(RecentlyPlayedSong unfinishedSong = null)
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

            if (unfinishedSong != null)
            {
                song = unfinishedSong.Song;
            }
            else
            {
                try
                {
                    if (DataManager.CurrentPlaylistId == 0)
                    {
                        Shell.Current.DisplayAlert("No Playlist", "No playlist was selected.", "OK");
                        return;
                    }

                    song = DataManager.GetNextSong(); // TODO: Make this method attempt to fetch from server if failure?
                }
                catch (InvalidOperationException)
                {
                    return;
                }
            }

            if (LocalFileExists(song.Directory, out string path))
            {
                songUrl = path;
            }
            else
            {
                songUrl = $"{SiteInfo.Url}music/{song.Directory}";
            }


            using (var media = new Media(_libvlc, new Uri(songUrl)))
            {
                _mediaPlayer = new MediaPlayer(media)
                {
                    EnableHardwareDecoding = true
                };

                _mediaPlayer.Play();

                if (unfinishedSong != null)
                {
                    _mediaPlayer.Time = unfinishedSong.TimestampSeconds.Value * 1000;
                }

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

            if (unfinishedSong == null)
            {
                DataManager.SetCurrentlyPlayingSong(song.SongId);
            }
        }

        private static bool LocalFileExists(string songFileName, out string path)
        {
            var fileService = DependencyService.Get<IFileService>();
            path = Path.Combine(fileService.GetRootPath(), LocalDirectories.Music, songFileName);
            
            if (File.Exists(path))
            {
                return true;
            }

            path = null;
            return false;
        }

        public static void Play()
        {
            if (_mediaPlayer == null)
            {
                var unfinishedSong = DataManager.GetCurrentlyPlayingSong();

                PlayNewSong(unfinishedSong);
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
