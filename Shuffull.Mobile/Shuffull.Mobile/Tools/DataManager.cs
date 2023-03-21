using Shuffull.Mobile.Constants;
using Shuffull.Mobile.Extensions;
using Shuffull.Mobile.Services;
using Shuffull.Shared.Networking.Models.Requests;
using Shuffull.Shared.Networking.Models.Results;
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
        private static List<Playlist> _playlists = new List<Playlist>();
        private static long _currentPlaylistId;
        private static readonly List<IRequest> _requests = new List<IRequest>();

        public static void Initialize()
        {
            // requests
            _requests.AddRange(ReadAll<IRequest>(LocalDirectories.Requests));

            // playlists
            _playlists.AddRange(ReadAll<Playlist>(LocalDirectories.Playlists));
        }

        private static List<T> ReadAll<T>(string directory)
        {
            var fileService = DependencyService.Get<IFileService>();
            var files = fileService.GetFiles(directory);
            var result = new List<T>();

            foreach (var file in files)
            {
                try
                {
                    var obj = fileService.ReadFile<T>(file);
                    result.Add(obj);
                }
                catch (SerializationException)
                {
                    fileService.DeleteFile(file);
                }
            }

            return result;
        }

        // Playlists
        public static void AddPlaylist(Playlist playlist)
        {
            WritePlaylist(playlist);
            _playlists.Add(playlist);
        }

        public static List<Playlist> GetPlaylists()
        {
            var result = new List<Playlist>(_playlists);
            return result.OrderBy(x => x.PlaylistId).ToList();
        }

        private static List<Playlist> ReadPlaylists(long[] playlistIds)
        {
            var fileService = DependencyService.Get<IFileService>();
            var result = new List<Playlist>();

            foreach (var playlistId in playlistIds)
            {
                try
                {
                    var playlist = fileService.ReadFile<Playlist>($"{LocalDirectories.Playlists}/{playlistId}");
                    result.Add(playlist);
                }
                catch
                {
                }
            }

            return result;
        }

        private static void WritePlaylist(Playlist playlist)
        {
            var fileService = DependencyService.Get<IFileService>();

            try
            {
                fileService.WriteFile(playlist, $"{LocalDirectories.Playlists}/{playlist.PlaylistId}");
            }
            catch
            {
            }
        }

        public static void SetCurrentPlaylist(long playlistId)
        {
            if (_playlists.Where(x => x.PlaylistId == playlistId).Any())
            {
                _currentPlaylistId = playlistId;
            }
        }

        public static void RemovePlaylist(Playlist playlist)
        {
            DeletePlaylist(playlist);
            _playlists.Remove(playlist);
        }
        private static void DeletePlaylist(Playlist playlist)
        {
            var fileService = DependencyService.Get<IFileService>();

            try
            {
                fileService.DeleteFile($"{LocalDirectories.Playlists}/{playlist.PlaylistId}");
            }
            catch
            {
            }
        }

        // Requests
        public static void AddRequest(IRequest request)
        {
            WriteRequest(request);
            _requests.Add(request);
        }

        public static void RemoveRequest(IRequest request)
        {
            DeleteRequest(request);
            _requests.Remove(request);
        }

        public static List<IRequest> GetRequests()
        {
            var result = new List<IRequest>(_requests);
            return result.OrderBy(x => x.TimeRequested).ToList();
        }

        private static void WriteRequest(IRequest request)
        {
            var fileService = DependencyService.Get<IFileService>();

            try
            {
                fileService.WriteFile(request, $"requests/{request.Guid}");
            }
            catch (Exception e)
            {
            }
        }

        private static void DeleteRequest(IRequest request)
        {
            var fileService = DependencyService.Get<IFileService>();

            try
            {
                fileService.DeleteFile($"{LocalDirectories.Requests}/{request.Guid}");
            }
            catch
            {
            }
        }

        public static string GetNextSong()
        {
            var playlist = _playlists.Where(x => x.PlaylistId == _currentPlaylistId).First();
            var randomIndex = new Random().Next(0, playlist.PlaylistSongs.Count());
            var randomSong = playlist
                .PlaylistSongs
                .Skip(randomIndex)
                .First();
            var result = $"{SiteInfo.URL}music/{randomSong.Song.Directory}";

            return result;
        }
    }
}
