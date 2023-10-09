﻿using LibVLCSharp.Shared;
using Microsoft.Extensions.DependencyInjection;
using Shuffull.Shared;
using Shuffull.Shared.Networking.Models;
using Shuffull.Shared.Networking.Models.Requests;
using Shuffull.Windows.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shuffull.Windows.Tools
{
    internal static class MediaManager
    {
        private static readonly LibVLC _libvlc = new();
        private static readonly MediaPlayer _mediaPlayer = new(_libvlc);
        private static readonly Queue<Song> _queue = new();
        private static readonly System.Timers.Timer _timer;
        public static long CurrentPlaylistId
        {
            get
            {
                return _currentPlaylistId;
            }
            set
            {
                if (_currentPlaylistId != -1)
                {
                    var context = Program.ServiceProvider.GetRequiredService<ShuffullContext>();
                    context.ClearRecentlyPlayedSongs();
                    context.SaveChangesAsync();
                }

                _currentPlaylistId = value;
            }
        }
        public static long RecentlyPlayedMaxCount = 25;
        private static long _currentPlaylistId = -1;
        private static bool _skipQueued = false;
        private static int decisecondCounter = 0;

        public static bool IsPlaying
        {
            get { return _mediaPlayer.IsPlaying; }
        }

        static MediaManager()
        {
            _timer = new System.Timers.Timer(100);
            _timer.Elapsed += OnTimerElapsed;
            _timer.Start();

            _mediaPlayer.EndReached += (sender, e) =>
            {
                // Running the skip function doesn't work here, so this is done instead
                _skipQueued = true;
            };
        }

        async private static void OnTimerElapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (_skipQueued)
            {
                _skipQueued = false;
                await Skip();
            }

            if (decisecondCounter == 0 && IsPlaying)
            {
                var context = Program.ServiceProvider.GetRequiredService<ShuffullContext>();

                context.UpdateCurrentlyPlayingSong((int)(_mediaPlayer.Time / 1000));
                await context.SaveChangesAsync();
            }

            decisecondCounter++;

            if (decisecondCounter > 10)
            {
                decisecondCounter = 0;
            }
        }

        /**
         * Only run at initialization
         */
        public static void QueueLastPlayedSong()
        {
            var context = Program.ServiceProvider.GetRequiredService<ShuffullContext>();
            var recentlyPlayedSong = context.GetCurrentlyPlayingSong();

            if (recentlyPlayedSong != null)
            {
                QueueSong(recentlyPlayedSong.Song);
            }
        }

        public static void QueueSong()
        {
            var context = Program.ServiceProvider.GetRequiredService<ShuffullContext>();

            if (!context.Playlists.Where(x => x.PlaylistId == CurrentPlaylistId).Any())
            {
                return;
            }

            var song = context.GetNextSong(CurrentPlaylistId);
            QueueSong(song);
        }

        public static void QueueSong(Song song)
        {
            _queue.Enqueue(song);
        }

        async private static Task Play(Song song, RecentlyPlayedSong? recentlyPlayedSong = null)
        {
            var context = Program.ServiceProvider.GetRequiredService<ShuffullContext>();
            var url = $"{SiteInfo.Url}music/{song.Directory}";
            var uri = new Uri(url);
            var media = new Media(_libvlc, uri);

            if (recentlyPlayedSong == null)
            {
                recentlyPlayedSong = context.GetCurrentlyPlayingSong();
                
                if (song.SongId != recentlyPlayedSong?.SongId)
                {
                    recentlyPlayedSong = null;
                }
            }

            _mediaPlayer.Play(media);

            if (recentlyPlayedSong != null)
            {
                _mediaPlayer.Time = (recentlyPlayedSong.TimestampSeconds ?? 0) * 1000;
                context.SetCurrentlyPlayingSong(song.SongId, recentlyPlayedSong.RecentlyPlayedSongGuid);
            }
            else
            {
                context.SetCurrentlyPlayingSong(song.SongId);
            }

            await context.SaveChangesAsync();

            var request = new UpdateSongLastPlayedRequest()
            {
                LastPlayed = DateTime.UtcNow,
                Guid = Guid.NewGuid().ToString(),
                SongId = song.SongId,
                TimeRequested = DateTime.UtcNow
            };
            await SyncManager.SubmitRequest(request);
            await SyncManager.RequestManualSync();
        }

        async public static Task Play(bool forceNew = false)
        {
            if (_mediaPlayer.Media == null || forceNew)
            {
                if (!_queue.Any())
                {
                    QueueSong();
                }

                var song = _queue.Dequeue();
                await Play(song);
            }
            else
            {
                _mediaPlayer.Play();
            }
        }

        public static async Task Previous()
        {
            var context = Program.ServiceProvider.GetRequiredService<ShuffullContext>();
            var recentlyPlayedSong = context.CheckForLastRecentlyPlayedSong();

            if (recentlyPlayedSong != null)
            {
                var song = recentlyPlayedSong.Song;
                await Play(song, recentlyPlayedSong);
            }
        }

        async public static Task Skip()
        {
            RecentlyPlayedSong? recentlyPlayedSong = null;
            Song song;

            if (_queue.Any())
            {
                song = _queue.Dequeue();
            }
            else
            {
                var context = Program.ServiceProvider.GetRequiredService<ShuffullContext>();
                recentlyPlayedSong = context.CheckForNextRecentlyPlayedSong();

                if (recentlyPlayedSong != null)
                {
                    song = recentlyPlayedSong.Song;
                }
                else
                {
                    song = context.GetNextSong(CurrentPlaylistId);
                }
            }

            if (recentlyPlayedSong != null)
            {
                await Play(song, recentlyPlayedSong);
            }
            else
            {
                await Play(song);
            }
        }

        public static void Pause()
        {
            if (_mediaPlayer.IsPlaying)
            {
                _mediaPlayer.Pause();
            }
        }
    }
}