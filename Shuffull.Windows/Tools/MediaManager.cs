using LibVLCSharp.Shared;
using Microsoft.Extensions.DependencyInjection;
using Shuffull.Shared;
using Shuffull.Shared.Networking.Models;
using Shuffull.Shared.Networking.Models.Requests;
using Shuffull.Shared.Networking.Models.Server;
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
                if (_currentPlaylistId == -1)
                {
                    using var context = Program.ServiceProvider.GetRequiredService<ShuffullContext>();
                    var localSessionData = context.LocalSessionData.First();
                    _currentPlaylistId = localSessionData.CurrentPlaylistId > 0 ? localSessionData.CurrentPlaylistId : -1;
                }

                return _currentPlaylistId;
            }
            set
            {
                using var context = Program.ServiceProvider.GetRequiredService<ShuffullContext>();
                var localSessionData = context.LocalSessionData.First();

                if (localSessionData.CurrentPlaylistId == value)
                {
                    return;
                }

                if (_currentPlaylistId != value)
                {
                    context.ClearRecentlyPlayedSongs();
                }

                _currentPlaylistId = value;
                localSessionData.CurrentPlaylistId = value;
                context.SaveChangesAsync();
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
                using var context = Program.ServiceProvider.GetRequiredService<ShuffullContext>();

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
            using var context = Program.ServiceProvider.GetRequiredService<ShuffullContext>();
            var recentlyPlayedSong = context.GetCurrentlyPlayingSong();

            if (recentlyPlayedSong != null)
            {
                QueueSong(recentlyPlayedSong.Song);
            }
        }

        public async static Task QueueSong()
        {
            using var context = Program.ServiceProvider.GetRequiredService<ShuffullContext>();

            if (!context.Playlists.Where(x => x.PlaylistId == CurrentPlaylistId).Any())
            {
                return;
            }

            var song = await context.GetNextSong(CurrentPlaylistId);
            QueueSong(song);
        }

        public static void QueueSong(Song song)
        {
            _queue.Enqueue(song);
        }

        public static void ClearQueue()
        {
            _queue.Clear();
        }

        async private static Task Play(Song song, RecentlyPlayedSong? recentlyPlayedSong = null)
        {
            using var context = Program.ServiceProvider.GetRequiredService<ShuffullContext>();
            var localSessionData = context.LocalSessionData.First();
            var url = $"{SiteInfo.Url}/music/{song.Directory}";
            var uri = new Uri(url);
            using var media = new Media(_libvlc, uri);

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

            var userSong = context.UserSongs
                .Where(x => x.UserId == localSessionData.UserId && x.SongId == song.SongId)
                .FirstOrDefault();

            if (userSong == null)
            {
                var newUserSongRequest = new CreateUserSongRequest()
                {
                    Guid = Guid.NewGuid().ToString(),
                    SongId = song.SongId,
                    UserId = localSessionData.UserId,
                    TimeRequested = DateTime.UtcNow
                };
                await SyncManager.SubmitRequest(newUserSongRequest);
            }

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
                    await QueueSong();
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
            using var context = Program.ServiceProvider.GetRequiredService<ShuffullContext>();
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
                using var context = Program.ServiceProvider.GetRequiredService<ShuffullContext>();
                recentlyPlayedSong = context.CheckForNextRecentlyPlayedSong();

                if (recentlyPlayedSong != null)
                {
                    song = recentlyPlayedSong.Song;
                }
                else
                {
                    song = await context.GetNextSong(CurrentPlaylistId);
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
