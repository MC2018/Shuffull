using Microsoft.EntityFrameworkCore;
using MoreLinq.Extensions;
using Newtonsoft.Json;
using Shuffull.Mobile.Constants;
using Shuffull.Mobile.Extensions;
using Shuffull.Mobile.Services;
using Shuffull.Shared;
using Shuffull.Shared.Networking.Models;
using Shuffull.Shared.Networking.Models.Requests;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using Xamarin.Forms;

namespace Shuffull.Mobile.Tools
{
    public class DataManager
    {
        // TODO: Timer to auto-update playlist information as needed
        private static ShuffullContext _context;
        private static long _currentPlaylistId = 0;
        private static object _lock = new object();

        public static long RecentlyPlayedMaxCount = 25;
        public static long CurrentPlaylistId
        {
            get
            {
                return _currentPlaylistId;
            }
            set
            {
                try
                {
                    Monitor.Enter(_lock);

                    if (_context.Playlists.Where(x => x.PlaylistId == value).Any())
                    {
                        _currentPlaylistId = value;
                    }
                }
                finally { Monitor.Exit(_lock); }
            }
        }

        public static void Initialize(string dbPath)
        {
            _context = new ShuffullContext(dbPath);

            if (_context.Database.GetPendingMigrations().Any())
            {
                try
                {
                    _context.Database.Migrate();
                }
                catch (Exception) // when the database changes too much, the schema needs to be rebuilt
                {
                    var fileService = DependencyService.Get<IFileService>();
                    fileService.DeleteFile(dbPath);
                    _context.Database.Migrate();
                }
            }
        }

        public static RecentlyPlayedSong GetCurrentlyPlayingSong()
        {
            try
            {
                Monitor.Enter(_lock);
                return _context.RecentlyPlayedSongs
                    .Where(x => x.TimestampSeconds != null)
                    .Include(x => x.Song)
                    .FirstOrDefault();
            }
            finally { Monitor.Exit(_lock); }
        }

        public static void ClearCurrentlyPlayingSong()
        {
            try
            {
                Monitor.Enter(_lock);
                var lastPlayedSong = _context.RecentlyPlayedSongs.Where(x => x.TimestampSeconds != null).FirstOrDefault();

                if (lastPlayedSong != null)
                {
                    lastPlayedSong.TimestampSeconds = null;
                }

                if (_context.RecentlyPlayedSongs.Count() > RecentlyPlayedMaxCount)
                {
                    var songToRemove = _context.RecentlyPlayedSongs.OrderBy(x => x.LastPlayed).First();
                    _context.RecentlyPlayedSongs.Remove(songToRemove);
                }
            } finally { Monitor.Exit(_lock); }
        }

        public static void SetCurrentlyPlayingSong(long songId, string recentlyPlayedSongGuid = null)
        {
            try
            {
                ClearCurrentlyPlayingSong();

                Monitor.Enter(_lock);
                var song = _context.Songs.Where(x => x.SongId == songId).FirstOrDefault();

                if (song == null)
                {
                    throw new Exception("No song available");
                }

                if (songId != -1)
                {
                    RecentlyPlayedSong recentlyPlayedSong;
                    var existingRecordFound = false;

                    if (recentlyPlayedSongGuid != null)
                    {
                        recentlyPlayedSong = _context.RecentlyPlayedSongs
                            .Where(x => x.RecentlyPlayedSongGuid == recentlyPlayedSongGuid)
                            .FirstOrDefault();

                        if (recentlyPlayedSong != null)
                        {
                            recentlyPlayedSong.TimestampSeconds = 0;
                            existingRecordFound = true;
                        }
                    }

                    if (!existingRecordFound)
                    {
                        recentlyPlayedSong = new RecentlyPlayedSong()
                        {
                            RecentlyPlayedSongGuid = Guid.NewGuid().ToString(),
                            SongId = songId,
                            TimestampSeconds = 0,
                            LastPlayed = DateTime.UtcNow
                        };
                        _context.RecentlyPlayedSongs.Add(recentlyPlayedSong);
                    }
                }

                _context.SaveChanges();
            }
            finally { Monitor.Exit(_lock); }
        }

        public static void UpdateCurrentlyPlayingSong(int timestampSeconds)
        {
            try
            {
                Monitor.Enter(_lock);
                var recentlyPlayedSong = _context.RecentlyPlayedSongs.Where(x => x.TimestampSeconds != null).FirstOrDefault();

                if (recentlyPlayedSong == null)
                {
                    throw new Exception("No song available");
                }

                recentlyPlayedSong.TimestampSeconds = timestampSeconds;
                _context.SaveChanges();
            }
            finally { Monitor.Exit(_lock); }
        }

        public static RecentlyPlayedSong CheckForLastRecentlyPlayedSong()
        {
            return CheckForRecentlyPlayedSong(false);
        }

        public static RecentlyPlayedSong CheckForNextRecentlyPlayedSong()
        {
            return CheckForRecentlyPlayedSong(true);
        }

        private static RecentlyPlayedSong CheckForRecentlyPlayedSong(bool next)
        {
            try
            {
                Monitor.Enter(_lock);
                var lastPlayed = _context.RecentlyPlayedSongs
                    .Where(x => x.TimestampSeconds != null)
                    .Select(x => x.LastPlayed)
                    .FirstOrDefault();

                if (lastPlayed == null)
                {
                    return null;
                }

                var query = _context.RecentlyPlayedSongs
                    .Include(x => x.Song)
                    .Where(x => next ? (x.LastPlayed > lastPlayed) : (x.LastPlayed < lastPlayed));

                if (next)
                {
                    query = query.OrderBy(x => x.LastPlayed);
                }
                else
                {
                    query = query.OrderByDescending(x => x.LastPlayed);
                }

                return query.FirstOrDefault();
            }
            finally { Monitor.Exit(_lock); }
        }

        // Playlists
        public static void UpdatePlaylist(Playlist playlist)
        {
            try
            {
                Monitor.Enter(_lock);
                var localPlaylist = _context.Playlists.Where(x => x.PlaylistId == playlist.PlaylistId).FirstOrDefault();

                if (localPlaylist == null)
                {
                    AddPlaylist(playlist, true);
                    return;
                }

                _context.Playlists.Remove(localPlaylist);
                _context.Playlists.Add(playlist);
                _context.SaveChanges();
            }
            finally { Monitor.Exit(_lock); }
        }

        private static void AddPlaylist(Playlist playlist, bool ignoreLock = false)
        {
            try
            {
                if (!ignoreLock)
                {
                    Monitor.Enter(_lock);
                }

                _context.Playlists.Add(playlist);
                _context.SaveChanges();
            }
            finally
            {
                if (!ignoreLock)
                {
                    Monitor.Exit(_lock);
                }
            }
        }

        public static List<Playlist> GetPlaylists(bool ignoreLock = false)
        {
            try
            {
                if (!ignoreLock)
                {
                    Monitor.Enter(_lock);
                }

                return _context.Playlists
                    .OrderBy(x => x.PlaylistId)
                    .ToList();
            }
            finally
            {
                if (!ignoreLock)
                {
                    Monitor.Exit(_lock);
                }
            }
        }

        public static Playlist GetPlaylistWithSongs(long playlistId)
        {
            try
            {
                Monitor.Enter(_lock);
                return _context.Playlists
                    .Where(x => x.PlaylistId == playlistId)
                    .Include(x => x.PlaylistSongs)
                    .ThenInclude(x => x.Song)
                    .OrderBy(x => x.PlaylistId)
                    .FirstOrDefault();
            }
            finally { Monitor.Exit(_lock); }
        }

        public static void RemovePlaylist(long playlistId)
        {
            try
            {
                Monitor.Enter(_lock);
                var playlist = _context.Playlists.Where(x => x.PlaylistId == playlistId).FirstOrDefault();

                if (playlist == null)
                {
                    return;
                }

                _context.Playlists.Remove(playlist);
                _context.SaveChanges();

                if (_currentPlaylistId == playlist.PlaylistId)
                {
                    _currentPlaylistId = GetPlaylists(true).FirstOrDefault()?.PlaylistId ?? 0;
                }
            }
            finally { Monitor.Exit(_lock); }
        }

        // Requests
        public static void AddRequest(Request request)
        {
            try
            {
                Monitor.Enter(_lock);
                _context.Requests.Add(request);
                _context.SaveChanges();
            }
            finally { Monitor.Exit(_lock); }
        }

        public static void RemoveRequest(Request request)
        {
            try
            {
                Monitor.Enter(_lock);
                _context.Requests.Remove(request);
                _context.SaveChanges();
            }
            finally { Monitor.Exit(_lock); }
        }

        public static List<Request> GetRequests()
        {
            try
            {
                Monitor.Enter(_lock);
                return _context.Requests
                    .OrderBy(x => x.TimeRequested)
                    .ToList();
            }
            finally { Monitor.Exit(_lock); }
        }

        public static Song GetNextSong()
        {
            try
            {
                Monitor.Enter(_lock);
                var songCount = _context.Playlists.Where(x => x.PlaylistId == CurrentPlaylistId)
                    .SelectMany(x => x.PlaylistSongs)
                    .Count();

                if (songCount == 0)
                {
                    throw new Exception("No song available");
                }

                var randomIndex = new Random().Next(0, songCount);
                var result = _context.Playlists
                    .SelectMany(x => x.PlaylistSongs)
                    .Skip(randomIndex)
                    .Take(1)
                    .Select(x => x.Song)
                    .First();

                return result;
            }
            finally { Monitor.Exit(_lock); }
        }

        internal static void IncrementPlaylistsBySongIds(List<long> songIds)
        {
            try
            {
                Monitor.Enter(_lock);
                var playlists = _context.Playlists
                    .Where(x => x.PlaylistSongs.Any(y => songIds.Contains(y.SongId)))
                    .DistinctBy(x => x.PlaylistId)
                    .ToList();

                foreach (var playlist in playlists)
                {
                    playlist.VersionCounter++;
                }

                _context.SaveChanges();
            }
            finally { Monitor.Exit(_lock); }
        }
    }
}
