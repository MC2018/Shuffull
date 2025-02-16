using Microsoft.Identity.Client;
using Shuffull.Site.Configuration;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Shuffull.Site.Models;
using System.Linq;
using System.Transactions;
using Newtonsoft.Json;
using Azure;
using OpenAI_API.Moderation;
using System.Text.RegularExpressions;
using Shuffull.Site.Models.Database;
using Shuffull.Shared.Tools;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;

namespace Shuffull.Site.Tools
{
    /// <summary>
    /// Handles logic regarding the downloading, file management, and database importing of songs
    /// </summary>
    public class SongImporter
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<SongImporter> _logger;
        private readonly ShuffullFilesConfiguration _fileConfig;
        private readonly string[] _audioExtensions = new string[] { ".mp3", ".wav", ".ogg", ".flac", ".m4a", ".aac" };
        private readonly string[] _mimeImageExtensions = new string[] { "image/jpeg", "image/png", "image/gif", "image/bmp" };

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="configuration">Configuration</param>
        /// <param name="services">Service provider</param>
        public SongImporter(IConfiguration configuration, IServiceProvider services, ILogger<SongImporter> logger)
        {
            _services = services;
            _fileConfig = configuration.GetSection(ShuffullFilesConfiguration.FilesConfigurationSection).Get<ShuffullFilesConfiguration>();
            _logger = logger;
        }

        // TODO: if two people upload at the same time and create the same new tag, it could create a duplicate!
        /// <summary>
        /// Imports a set of music into the database and attaches them to a playlist
        /// </summary>
        /// <param name="path">Path where the downloaded music files exist</param>
        /// <param name="playlistId">Playlist to add the music to</param>
        private void ImportFiles(string path = "", long playlistId = -1, bool manual = false)
        {
            path = manual ? _fileConfig.ManualSongImportDirectory : path;
            var jsonFiles = Directory.GetFiles(path, "*.json").OrderBy(x => x).ToList();
            var songFiles = Directory.GetFiles(path).Where(x => Regex.Match(x, "\\.(mp3|wav)$").Success).ToList();
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            using var transaction = context.Database.BeginTransaction();
            var allTags = context.Tags.AsNoTracking().ToList();
            var playlist = context.Playlists.Where(x => x.PlaylistId == playlistId).FirstOrDefault();
            var songArtistPairs = new Dictionary<Song, List<Artist>>();
            var songTagPairs = new Dictionary<Song, List<Tag>>();
            var songs = new List<Song>();
            var newArtists = new List<Artist>();

            foreach (var oldSongFile in songFiles)
            {
                var fileExtension = Path.GetExtension(oldSongFile).ToLowerInvariant();
                var artists = new List<Artist>();
                string newSongFile;

                if (!_audioExtensions.Contains(fileExtension))
                {
                    AttemptMarkingAsFailure(oldSongFile);
                    continue;
                }

                try
                {
                    newSongFile = MoveAndRenameSong(oldSongFile);
                }
                catch (IOException)
                {
                    AttemptMarkingAsFailure(oldSongFile);
                    continue;
                }

                var openAiManager = _services.GetRequiredService<OpenAIManager>();
                var musicFile = TagLib.File.Create(newSongFile);
                var song = new Song()
                {
                    Name = musicFile.Tag.Title ?? Path.GetFileNameWithoutExtension(oldSongFile),
                    FileExtension = Path.GetExtension(newSongFile),
                    FileHash = Path.GetFileNameWithoutExtension(newSongFile)
                };
                songs.Add(song);

                // Add artists
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
                TagsResponse? tagsResponse = null;

                if (manual)
                {
                    var jsonFileIndex = jsonFiles.BinarySearch($"{oldSongFile}.json");

                    if (jsonFileIndex != -1)
                    {
                        var jsonFile = jsonFiles[jsonFileIndex];
                        tagsResponse = JsonConvert.DeserializeObject<TagsResponse>(File.ReadAllText(jsonFile)) ?? new TagsResponse();
                    }
                }
                else if (openAiManager.Enabled)
                {
                    tagsResponse = openAiManager.RequestTagsResponse(song, artists, allTags).Result;
                }

                if (tagsResponse != null)
                {
                    var responseList = tagsResponse.ToTagList();
                    var responseNames = responseList.Select(x => x.Name).ToList();
                    var tagsToApply = new TagsToApply();
                    var newTags = tagsToApply.NewTags.Where(x => !allTags.Select(y => y.Name).Contains(x.Name));

                    tagsToApply.ExistingTags.AddRange(allTags.Where(x => responseNames.Contains(x.Name)));
                    tagsToApply.NewTags.AddRange(
                        responseList.Where(x => x.Type != Enums.TagType.Genre && !tagsToApply.ExistingTags.Select(y => y.Name).Contains(x.Name))
                        );

                    songTagPairs.Add(song, tagsToApply.NewTags.Concat(tagsToApply.ExistingTags).ToList());

                    if (newTags.Any())
                    {
                        context.Tags.AddRange(newTags);
                        context.SaveChanges();
                        allTags.AddRange(newTags);
                    }
                }

                // Save album art
                Image newAlbumArt;
                var resolution = 512;

                if (musicFile.Tag.Pictures.Length > 0)
                {
                    var picture = musicFile.Tag.Pictures[0];
                    var mimeType = picture.MimeType;

                    if (_mimeImageExtensions.Contains(mimeType))
                    {
                        using var ms = new MemoryStream(picture.Data.Data);
                        using var rawAlbumArt = Image.Load(ms);

                        if (rawAlbumArt.Width != resolution || rawAlbumArt.Height != resolution)
                        {
                            newAlbumArt = ImageManipulator.ResizeWithPadding(rawAlbumArt, resolution, resolution);
                        }
                        else
                        {
                            newAlbumArt = rawAlbumArt;
                        }
                    }
                    else
                    {
                        newAlbumArt = ImageManipulator.GenerateDefaultImage(resolution, resolution);
                    }
                }
                else
                {
                    newAlbumArt = ImageManipulator.GenerateDefaultImage(resolution, resolution);
                }

                var outputFilePath = Path.Combine(_fileConfig.AlbumArtDirectory, $"{Path.GetFileNameWithoutExtension(song.FileHash)}.jpg");
                newAlbumArt.Save(outputFilePath, new JpegEncoder());
            }

            if (newArtists.Any())
            {
                context.Artists.AddRange(newArtists);
                context.SaveChanges();
            }

            context.Songs.AddRange(songs);
            context.SaveChanges();

            if (playlist != null)
            {
                foreach (var song in songs)
                {
                    var playlistSong = new PlaylistSong()
                    {
                        PlaylistId = playlistId,
                        SongId = song.SongId,
                    };
                    context.PlaylistSongs.Add(playlistSong);
                }
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
                foreach (var tag in songTagPair.Value)
                {
                    var songTag = new SongTag()
                    {
                        SongId = songTagPair.Key.SongId,
                        TagId = tag.TagId
                    };
                    context.SongTags.Add(songTag);
                }
            }

            // Save
            context.SaveChanges();
            transaction.Commit();

            // Delete contents
            var directoryInfo = new DirectoryInfo(path);

            foreach (var directory in directoryInfo.GetDirectories())
            {
                directory.Delete(true);
            }

            foreach (var file in directoryInfo.GetFiles())
            {
                file.Delete();
            }
        }

        public void ImportManualFiles()
        {
            ImportFiles(manual: true);
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
        /// <param name="currentSongPath">Current song path</param>
        /// <returns>New song directory</returns>
        private string MoveAndRenameSong(string currentSongPath)
        {
            var hexStr = Hasher.ShaHash(currentSongPath).Substring(0, 32);
            var fileExtension = Path.GetExtension(currentSongPath).ToLower();
            var newSongName = Path.GetFileName($"{hexStr}{fileExtension}");
            var newSongPath = Path.Combine(_fileConfig.MusicRootDirectory, newSongName);

            if (File.Exists(newSongPath))
            {
                File.Delete(newSongPath);
            }

            File.Move(currentSongPath, newSongPath);

            return newSongPath;
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
