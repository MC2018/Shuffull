using Microsoft.Identity.Client;
using Murmur;
using Shuffull.Site.Database.Models;
using Shuffull.Site.Configuration;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;

namespace Shuffull.Site.Tools
{
    /// <summary>
    /// Handles logic regarding the downloading, file management, and database importing of songs
    /// </summary>
    public class SongImporter
    {
        private readonly IServiceProvider _services;
        private readonly ShuffullFilesConfiguration _fileConfig;
        private readonly string[] _audioExtensions = new string[] { ".mp3", ".wav", ".ogg", ".flac", ".m4a", ".aac" };

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="configuration">Configuration</param>
        /// <param name="services">Service provider</param>
        public SongImporter(IConfiguration configuration, IServiceProvider services)
        {
            _services = services;
            _fileConfig = configuration.GetSection(ShuffullFilesConfiguration.FilesConfigurationSection).Get<ShuffullFilesConfiguration>();
        }

        /// <summary>
        /// Imports a set of music into the database and attaches them to a playlist
        /// </summary>
        /// <param name="path">Path where the downloaded music files exist</param>
        /// <param name="playlistId">Playlist to add the music to</param>
        private void ImportFiles(string path, long playlistId)
        {
            var files = Directory.GetFiles(path);
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            var allTags = context.Tags.AsNoTracking().ToList();
            var playlist = context.Playlists.Where(x => x.PlaylistId == playlistId).First();
            var songArtistPairs = new Dictionary<Song, List<Artist>>();
            var songTagPairs = new Dictionary<Song, List<Tag>>();
            var songs = new List<Song>();
            var newArtists = new List<Artist>();
            var newTags = new List<Tag>();

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
                    newFile = MoveAndRenameSong(oldFile);
                }
                catch (IOException)
                {
                    AttemptMarkingAsFailure(oldFile);
                    continue;
                }

                var openAiManager = _services.GetRequiredService<OpenAIManager>();
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

                // Adding tags
                if (openAiManager.Enabled)
                {
                    var tagsToApply = openAiManager.RequestTagsToApply(song, artists, allTags).Result;
                    songTagPairs.Add(song, tagsToApply.NewTags.Concat(tagsToApply.ExistingTags).ToList());
                    newTags.AddRange(tagsToApply.NewTags.Where(x => !newTags.Select(y => y.Name).Contains(x.Name)));
                }
            }

            if (newTags.Any())
            {
                context.Tags.AddRange(newTags);
                context.SaveChanges();
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

            foreach (var songTagPair in songTagPairs)
            {
                foreach ( var tag in songTagPair.Value)
                {
                    var songTag = new SongTag()
                    {
                        SongId = songTagPair.Key.SongId,
                        TagId = tag.TagId
                    };
                    context.SongTags.Add(songTag);
                }
            }

            context.SaveChanges();
            Directory.Delete(path, true);
        }

        /// <summary>
        /// Downloads a list of files sent in, imports them, and adds them to a playlist
        /// </summary>
        /// <param name="files">List of music files to download</param>
        /// <param name="playlistId">Playlist to add the music to</param>
        public void DownloadAndImportFiles(IEnumerable<IFormFile> files, long playlistId)
        {
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            var path = Path.Combine(_fileConfig.SongImportDirectory, Guid.NewGuid().ToString().ToLower());

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

        /// <summary>
        /// Moves a song from the temporary download folder to its permanent location and renames the file to a UUID
        /// </summary>
        /// <param name="currentSongDirectory">Current song directory</param>
        /// <returns>New song directory</returns>
        private string MoveAndRenameSong(string currentSongDirectory)
        {
            var murmur128 = MurmurHash.Create128();
            var fileBytes = File.ReadAllBytes(currentSongDirectory);
            var hashValue = murmur128.ComputeHash(fileBytes);
            var hexStr = BitConverter.ToString(hashValue);

            var fileExtension = Path.GetExtension(currentSongDirectory);
            var newSongName = Path.GetFileName($"{hexStr}{fileExtension}");
            var newSongDirectory = Path.Combine(_fileConfig.MusicRootDirectory, newSongName.Replace("-", "").ToLower());

            File.Move(currentSongDirectory, newSongDirectory);

            return newSongDirectory;
        }

        /// <summary>
        /// Attempt to a song into the failed import directory
        /// </summary>
        /// <param name="songDirectory">Song directory</param>
        private void AttemptMarkingAsFailure(string songDirectory)
        {
            try
            {
                Directory.Move(songDirectory, Path.Combine(_fileConfig.FailedImportDirectory, Path.GetFileName(songDirectory)));
            }
            catch { }
        }
    }
}
