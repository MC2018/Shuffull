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
using Xamarin.Forms;

namespace Shuffull.Mobile.Tools
{
    public class DataManager
    {
        // TODO: Timer to auto-update playlist information as needed
        private static long _currentPlaylistId;

        public static long CurrentPlaylistId
        {
            get
            {
                if (_currentPlaylistId == 0)
                {
                    var context = DependencyService.Get<ShuffullContext>();
                    var playlist = context.Playlists.FirstOrDefault();

                    if (playlist != null)
                    {
                        _currentPlaylistId = playlist.PlaylistId;
                    }
                }

                return _currentPlaylistId;
            }
            set
            {
                var context = DependencyService.Get<ShuffullContext>();

                if (context.Playlists.Where(x => x.PlaylistId == value).Any())
                {
                    _currentPlaylistId = value;
                }
            }
        }

        // Playlists
        public static void UpdatePlaylist(Playlist playlist)
        {
            var context = DependencyService.Get<ShuffullContext>();
            var localPlaylist = context.Playlists.Where(x => x.PlaylistId == playlist.PlaylistId).FirstOrDefault();

            if (localPlaylist == null)
            {
                AddPlaylist(playlist);
                return;
            }

            context.Playlists.Remove(localPlaylist);
            context.Playlists.Add(playlist);
            context.SaveChanges();
        }

        private static void AddPlaylist(Playlist playlist)
        {
            var context = DependencyService.Get<ShuffullContext>();
            context.Playlists.Add(playlist);
            context.SaveChanges();
        }

        public static List<Playlist> GetPlaylists()
        {
            var context = DependencyService.Get<ShuffullContext>();
            return context.Playlists
                .OrderBy(x => x.PlaylistId)
                .ToList();
        }

        public static void RemovePlaylist(long playlistId)
        {
            var context = DependencyService.Get<ShuffullContext>();
            var playlist = context.Playlists.Where(x => x.PlaylistId == playlistId).FirstOrDefault();

            if (playlist == null)
            {
                return;
            }

            context.Playlists.Remove(playlist);
            context.SaveChanges();
            
            if (_currentPlaylistId == playlist.PlaylistId)
            {
                _currentPlaylistId = GetPlaylists().FirstOrDefault()?.PlaylistId ?? 0;
            }
        }

        // Requests
        public static void AddRequest(Request request)
        {
            var context = DependencyService.Get<ShuffullContext>();
            context.Requests.Add(request);
            context.SaveChanges();
        }

        public static void RemoveRequest(Request request)
        {
            var context = DependencyService.Get<ShuffullContext>();
            context.Requests.Remove(request);
            context.SaveChanges();
        }

        public static List<Request> GetRequests()
        {
            var context = DependencyService.Get<ShuffullContext>();
            return context.Requests
                .OrderBy(x => x.TimeRequested)
                .ToList();
        }

        public static Song GetNextSong()
        {
            var context = DependencyService.Get<ShuffullContext>();
            var songCount = context.Playlists.Where(x => x.PlaylistId == CurrentPlaylistId)
                .SelectMany(x => x.PlaylistSongs)
                .Count();

            if (songCount == 0)
            {
                throw new Exception("No song available");
            }

            var randomIndex = new Random().Next(0, songCount);
            var result = context.Playlists
                .SelectMany(x => x.PlaylistSongs)
                .Skip(randomIndex)
                .Take(1)
                .Select(x => x.Song)
                .First();

            return result;
        }

        internal static void IncrementPlaylistsBySongIds(List<long> songIds)
        {
            var context = DependencyService.Get<ShuffullContext>();
            var playlists = context.Playlists
                .Where(x => x.PlaylistSongs.Any(y => songIds.Contains(y.SongId)))
                .DistinctBy(x => x.PlaylistId)
                .ToList();

            foreach (var playlist in playlists)
            {
                playlist.VersionCounter++;
            }

            context.SaveChanges();
        }
    }
}
