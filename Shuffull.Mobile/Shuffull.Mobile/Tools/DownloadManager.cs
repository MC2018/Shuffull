using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Shuffull.Mobile.Constants;
using Shuffull.Mobile.Extensions;
using Shuffull.Mobile.Services;
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
using Xamarin.Forms;

namespace Shuffull.Mobile.Tools
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
            var fileService = DependencyService.Get<IFileService>();
            var destination = Path.Combine(fileService.GetRootPath(), LocalDirectories.Music, songFileName);

            if (File.Exists(destination))
            {
                return;
            }

            _songFileNames.Add(songFileName);
        }

        private static void Download(string songFileName)
        {
            var fileService = DependencyService.Get<IFileService>();
            var url = Path.Combine(SiteInfo.Url, "music", songFileName);
            var directory = Path.Combine(fileService.GetRootPath(), LocalDirectories.Music);
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
