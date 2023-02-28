using Microsoft.Identity.Client;
using Murmur;
using Shuffull.Shared;
using Shuffull.Shared.Models;
using Shuffull.Site.Configuration;
using Shuffull.Site.Logic;
using System.Security.Cryptography;

namespace Shuffull.Site.Services
{
    public class SongImportService : IHostedService
    {

        private IServiceProvider _services;
        private Timer _timer;
        private string _songImportDirectory;
        private string _failedImportDirectory;
        private string[] _audioExtensions = new string[] { ".mp3", ".wav", ".ogg", ".flac", ".m4a", ".aac" };
        private bool _inProgress = false;

        public SongImportService(IConfiguration configuration, IServiceProvider services)
        {
            _services = services;
            _songImportDirectory = configuration.GetSection(ShuffullFilesConfiguration.FilesConfigurationSection).Get<ShuffullFilesConfiguration>().SongImportDirectory;
            _failedImportDirectory = configuration.GetSection(ShuffullFilesConfiguration.FilesConfigurationSection).Get<ShuffullFilesConfiguration>().FailedImportDirectory;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _timer = new Timer(OnCallBack, null, 5000, 60000);
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await _timer.DisposeAsync();
        }

        private void OnCallBack(object? state)
        {
            if (_inProgress)
            {
                return;
            }

            _inProgress = true;

            var files = Directory.GetFiles(_songImportDirectory);
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            var songArtistPairs = new Dictionary<Song, List<Artist>>();

            foreach (var oldFile in files)
            {
                var fileExtension = Path.GetExtension(oldFile).ToLowerInvariant();
                var artists = new List<Artist>();
                string newFile;

                if (!_audioExtensions.Contains(fileExtension))
                {
                    AttemptMarkingAsFailure(oldFile);
                    continue;
                }

                try
                {
                    newFile = MoveAndRenameFile(oldFile);
                }
                catch (IOException)
                {
                    AttemptMarkingAsFailure(oldFile);
                    continue;
                }

                var musicFile = TagLib.File.Create(newFile);
                var song = new Song()
                {
                    Name = musicFile.Tag.Title ?? Path.GetFileNameWithoutExtension(musicFile.Name),
                    Directory = Path.GetFileName(newFile)
                };

                context.Songs.Add(song);

                foreach (var performer in musicFile.Tag.Performers)
                {
                    var artist = new Artist()
                    {
                        Name = performer
                    };

                    context.Artists.Add(artist);
                    artists.Add(artist);
                }

                songArtistPairs.Add(song, artists);
            }

            context.SaveChanges();

            foreach (var songArtistPair in songArtistPairs)
            {
                foreach (var artist in songArtistPair.Value)
                {
                    var songArtist = new SongArtist()
                    {
                        SongId = songArtistPair.Key.SongId,
                        ArtistId = artist.ArtistId
                    };

                    context.SongArtists.Add(songArtist);
                }
            }

            context.SaveChanges();

            _inProgress = false;
        }

        private string MoveAndRenameFile(string currentFileDirectory)
        {
            //using var sha256 = SHA256.Create();
            var murmur128 = MurmurHash.Create128();
            var fileBytes = File.ReadAllBytes(currentFileDirectory);
            var hashValue = murmur128.ComputeHash(fileBytes);
            var hexStr = BitConverter.ToString(hashValue);

            var fileExtension = Path.GetExtension(currentFileDirectory);
            var newFileName = Path.GetFileName($"{hexStr}{fileExtension}");
            var newFileDirectory = Path.Combine(FileRetrieval.RootDirectory, newFileName).Replace("-", "").ToLower();

            File.Move(currentFileDirectory, newFileDirectory);

            return newFileDirectory;
        }

        private void AttemptMarkingAsFailure(string fileDirectory)
        {
            try
            {
                Directory.Move(fileDirectory, Path.Combine(_failedImportDirectory, Path.GetFileName(fileDirectory)));
            }
            catch { }
        }
    }
}
