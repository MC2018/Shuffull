using Microsoft.Identity.Client;
using Murmur;
using Shuffull.Site.Database.Models;
using Shuffull.Site.Configuration;
using Shuffull.Site.Tools;
using System.Security.Cryptography;

namespace Shuffull.Site.Services
{
    public class SongImportService
    {
        private IServiceProvider _services;
        private string _songImportDirectory;
        private string _failedImportDirectory;
        private string[] _audioExtensions = new string[] { ".mp3", ".wav", ".ogg", ".flac", ".m4a", ".aac" };

        public SongImportService(IConfiguration configuration, IServiceProvider services)
        {
            _services = services;
            _songImportDirectory = configuration.GetSection(ShuffullFilesConfiguration.FilesConfigurationSection).Get<ShuffullFilesConfiguration>().SongImportDirectory;
            _failedImportDirectory = configuration.GetSection(ShuffullFilesConfiguration.FilesConfigurationSection).Get<ShuffullFilesConfiguration>().FailedImportDirectory;
        }

        private void ImportFiles(string path, long playlistId)
        {
            var files = Directory.GetFiles(path);
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            var playlist = context.Playlists.Where(x => x.PlaylistId == playlistId).First();
            var songArtistPairs = new Dictionary<Song, List<Artist>>();
            var songs = new List<Song>();
            var newArtists = new List<Artist>();

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
                songs.Add(song);

                var performerNames = musicFile.Tag.Performers;

                for (int i = 0; i < performerNames.Length; i++)
                {
                    var performerName = performerNames[i];
                    var nextPerformerName = performerNames.Length > i + 1 ? performerNames[i + 1] : string.Empty;

                    if (performerName.EndsWith("AC") && nextPerformerName.StartsWith("DC"))
                    {
                        performerName += $"/{nextPerformerName}";
                        i++;
                    }

                    // TODO: try figuring out a better way of pulling this data from SQL
                    var existingArtist = context.Artists.SingleOrDefault(a => a.Name == performerName);
                    var newArtist = newArtists.Where(x => x.Name == performerName).FirstOrDefault();

                    if (newArtist != null)
                    {
                        existingArtist = newArtist;
                    }
                    else if (existingArtist == null)
                    {
                        existingArtist = new Artist()
                        {
                            Name = performerName
                        };
                        newArtists.Add(existingArtist);
                    }

                    artists.Add(existingArtist);
                }

                songArtistPairs.Add(song, artists);
            }

            if (newArtists.Any())
            {
                context.Artists.AddRange(newArtists);
                context.SaveChanges();
            }

            context.Songs.AddRange(songs);
            context.SaveChanges();

            foreach (var song in songs)
            {
                var playlistSong = new PlaylistSong()
                {
                    PlaylistId = playlistId,
                    SongId = song.SongId,
                    InQueue = true
                };
                context.PlaylistSongs.Add(playlistSong);
            }

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
            Directory.Delete(path, true);
        }

        public void DownloadAndImportFiles(IEnumerable<IFormFile> files, long playlistId)
        {
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            var path = Path.Combine(_songImportDirectory, Guid.NewGuid().ToString().ToLower());

            Directory.CreateDirectory(path);

            foreach (var file in files)
            {
                var filePath = Path.Combine(path, file.FileName);
                using var stream = new FileStream(filePath, FileMode.Create);
                
                file.CopyTo(stream);
            }

            Task.Run(() =>
            {
                ImportFiles(path, playlistId);
            });
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
            var newFileDirectory = Path.Combine(FileRetrieval.RootDirectory, newFileName.Replace("-", "").ToLower());

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
