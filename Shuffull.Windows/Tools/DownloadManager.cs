using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Shuffull.Windows.Constants;
using Shuffull.Shared;
using Shuffull.Shared.Enums;
using Shuffull.Shared.Networking.Models;
using Shuffull.Shared.Networking.Models.Requests;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.DependencyInjection;

namespace Shuffull.Windows.Tools
{
    public static class DownloadManager
    {
        private static readonly List<string> _songFileNames = new List<string>();
        private static readonly System.Timers.Timer _timer;
        private static bool _currentlyDownloading = false;

        static DownloadManager()
        {
            _timer = new System.Timers.Timer(100);
            _timer.Elapsed += OnTimerElapsed;
            _timer.Start();
        }

        private static void OnTimerElapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (_currentlyDownloading)
            {
                return;
            }

            if (!_songFileNames.Any())
            {
                return;
            }

            _currentlyDownloading = true;

            var songFileName = _songFileNames[0];
            _songFileNames.RemoveAt(0);
            Download(songFileName);

            _currentlyDownloading = false;
        }

        public static void AddToQueue(string songFileName)
        {
            var destination = Path.Combine(Directories.MusicFolderAbsolutePath, songFileName);

            if (File.Exists(destination))
            {
                return;
            }

            _songFileNames.Add(songFileName);
        }

        private static void Download(string songFileName)
        {
            var url = Path.Combine(SiteInfo.Url, "music", songFileName);
            var directory = Directories.MusicFolderAbsolutePath;
            var destination = Path.Combine(directory, songFileName);

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            using (var webClient = new WebClient())
            {
                webClient.DownloadFile(url, destination);
            }
        }
    }
}
