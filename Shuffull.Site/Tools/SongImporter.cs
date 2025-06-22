using Microsoft.Identity.Client;
using Shuffull.Site.Configuration;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Shuffull.Site.Models;
using System.Linq;
using System.Transactions;
using Newtonsoft.Json;
using Azure;
using System.Text.RegularExpressions;
using Shuffull.Site.Models.Database;
using Shuffull.Shared.Tools;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using Shuffull.Shared.Enums;
using Shuffull.Site.Tools.AI;
using Shuffull.Site.Services.FileStorage;
using Nut.Results;

namespace Shuffull.Site.Tools;

/// <summary>
/// Handles logic regarding the downloading, file management, and database importing of songs
/// </summary>
public class SongImporter
{
    private readonly IServiceProvider _services;
    private readonly ILogger<SongImporter> _logger;
    private readonly ShuffullFilesConfiguration _fileConfig;
    private readonly IFileStorageService _fileStorageService;
    private readonly string[] _audioExtensions = new string[] { ".mp3", ".wav" };
    private readonly string[] _mimeImageExtensions = new string[] { "image/jpeg", "image/png", "image/gif", "image/bmp" };

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="configuration">Configuration</param>
    /// <param name="services">Service provider</param>
    public SongImporter(IConfiguration configuration, IServiceProvider services, ILogger<SongImporter> logger, IFileStorageService fileStorageService)
    {
        _services = services;
        _fileConfig = configuration.GetSection(ShuffullFilesConfiguration.FilesConfigurationSection).Get<ShuffullFilesConfiguration>() ?? throw new ArgumentNullException(nameof(_fileConfig));
        _logger = logger;
        _fileStorageService = fileStorageService ?? throw new ArgumentNullException(nameof(fileStorageService));
    }

    // TODO: if two people upload at the same time and create the same new tag, it could create a duplicate!
    /// <summary>
    /// Imports a set of music into the database and attaches them to a playlist
    /// </summary>
    /// <param name="path">Path where the downloaded music files exist</param>
    /// <param name="playlistId">Playlist to add the music to</param>
    private async Task<Result> ImportFilesAsync(string path = "", string? playlistId = null, bool manual = false)
    {
        path = manual ? _fileConfig.ManualSongImportDirectory : path;
        using var scope = _services.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
        using var transaction = context.Database.BeginTransaction();
        var allTags = context.Tags.AsNoTracking().ToList();
        var playlist = context.Playlists.Where(x => x.PlaylistId == playlistId).FirstOrDefault();
        var songArtistPairs = new Dictionary<Song, List<Artist>>();
        var songTagPairs = new Dictionary<Song, List<Tag>>();
        var songs = new List<Song>();
        var newArtists = new List<Artist>();

        var songFilesResult = await _fileStorageService.GetFilesAsync(path);
        if (songFilesResult.IsError)
        {
            return songFilesResult.PreserveErrorAs();
        }
        var songFiles = songFilesResult.Get().Where(x => Regex.Match(x, "\\.(mp3|wav)$").Success).ToList();

        foreach (var oldSongFile in songFiles)
        {
            var fileExtension = Path.GetExtension(oldSongFile).ToLowerInvariant();
            var artists = new List<Artist>();
            string newSongPath;

            if (!_audioExtensions.Contains(fileExtension))
            {
                await AttemptMarkingAsFailure(oldSongFile);
                continue;
            }

            var moveAndRenameResult = await MoveAndRenameSong(oldSongFile);
            if (moveAndRenameResult.IsError)
            {
                await AttemptMarkingAsFailure(oldSongFile);
                continue;
            }
            newSongPath = moveAndRenameResult.Get();

            var aiManager = _services.GetService<IAIManager>();
            var musicFile = TagLib.File.Create(newSongPath);
            var song = new Song()
            {
                SongId = Ulid.NewUlid().ToString(),
                Name = musicFile.Tag.Title ?? Path.GetFileNameWithoutExtension(oldSongFile),
                FileExtension = Path.GetExtension(newSongPath),
                FileHash = Path.GetFileNameWithoutExtension(newSongPath)
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
                        ArtistId = Ulid.NewUlid().ToString(),
                        Name = performerName
                    };
                    newArtists.Add(existingArtist);
                }

                artists.Add(existingArtist);
            }

            songArtistPairs.Add(song, artists);

            // Adding tags
            GenerateTagsResponse? generateTagsResponse = null;

            if (manual)
            {
                var jsonFilesResult = await _fileStorageService.GetFilesAsync(path, "*.json");
                if (jsonFilesResult.IsError)
                {
                    return jsonFilesResult.PreserveErrorAs();
                }
                var jsonFiles = jsonFilesResult.Get();
                var jsonFileIndex = jsonFiles.BinarySearch($"{oldSongFile}.json");

                if (jsonFileIndex != -1)
                {
                    var jsonFile = jsonFiles[jsonFileIndex];
                    generateTagsResponse = JsonConvert.DeserializeObject<GenerateTagsResponse>(File.ReadAllText(jsonFile)) ?? new GenerateTagsResponse();
                }
            }
            else if (aiManager != null)
            {
                var allGenreTags = allTags.Where(x => x.Type == TagType.Genre).ToList();
                generateTagsResponse = aiManager.GenerateTagsAsync(song, artists, allGenreTags).Result;
            }

            if (generateTagsResponse != null)
            {
                var responseList = generateTagsResponse.ToTagList();
                var responseNames = responseList.Select(x => x.Name).ToList();
                var tagsToApply = new TagsToApply();
                var newTags = tagsToApply.NewTags.Where(x => !allTags.Select(y => y.Name).Contains(x.Name));

                tagsToApply.ExistingTags.AddRange(allTags.Where(x => responseNames.Contains(x.Name)));
                tagsToApply.NewTags.AddRange(
                    responseList.Where(x => x.Type != TagType.Genre && !tagsToApply.ExistingTags.Select(y => y.Name).Contains(x.Name))
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
                    PlaylistSongId = Ulid.NewUlid().ToString(),
                    PlaylistId = playlist.PlaylistId,
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
                    SongArtistId = Ulid.NewUlid().ToString(),
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
                    SongTagId = Ulid.NewUlid().ToString(),
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
        var deleteDirectoryResult = await _fileStorageService.DeleteDirectoryAsync(path);
        if (deleteDirectoryResult.IsError)
        {
            return deleteDirectoryResult;
        }

        return Result.Ok();
    }

    public async Task ImportManualFiles()
    {
        await ImportFilesAsync(manual: true);
    }

    /// <summary>
    /// Downloads a list of files sent in, imports them, and adds them to a playlist
    /// </summary>
    /// <param name="files">List of music files to download</param>
    /// <param name="playlistId">Playlist to add the music to</param>
    public async Task<Result> DownloadAndImportFilesAsync(IEnumerable<IFormFile> files, string playlistId)
    {
        using var scope = _services.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
        var path = Path.Combine(_fileConfig.SongImportDirectory, Ulid.NewUlid().ToString().ToLower());

        foreach (var file in files)
        {
            var filePath = Path.Combine(path, file.FileName);
            var uploadFileResult = await _fileStorageService.UploadFileAsync(filePath, file, true);
            if (uploadFileResult.IsError)
            {
                return uploadFileResult;
            }
        }

        // Start the import process in the background
        Task.Run(async () =>
        {
            await ImportFilesAsync(path, playlistId);
        });
        return Result.Ok();
    }

    /// <summary>
    /// Moves a song from the temporary download folder to its permanent location and renames the file to a SHA hash
    /// </summary>
    /// <param name="currentSongPath">Current song path</param>
    /// <returns>New song path</returns>
    private async Task<Result<string>> MoveAndRenameSong(string currentSongPath)
    {
        // TODO: shouldn't need to re-download here
        var fileBytesResult = await _fileStorageService.DownloadFileBytesAsync(currentSongPath);
        if (fileBytesResult.IsError)
        {
            return fileBytesResult.PreserveErrorAs<string>();
        }
        var fileBytes = fileBytesResult.Get();
        var hexStr = Hasher.ShaHash(fileBytes).Substring(0, 32);
        var fileExtension = Path.GetExtension(currentSongPath).ToLower();
        var newSongName = Path.GetFileName($"{hexStr}{fileExtension}");
        var newSongPath = Path.Combine(_fileConfig.MusicRootDirectory, newSongName);

        var moveFileResult = await _fileStorageService.MoveFileAsync(currentSongPath, newSongPath, true);
        if (moveFileResult.IsError)
        {
            return moveFileResult.PreserveErrorAs<string>();
        }

        return newSongPath;
    }

    /// <summary>
    /// Attempt to a song into the failed import directory
    /// </summary>
    /// <param name="songPath">Song directory</param>
    private async Task<Result> AttemptMarkingAsFailure(string songPath)
    {
        var newSongPath = Path.Combine(_fileConfig.FailedImportDirectory, Path.GetFileName(songPath));
        var moveFileResult = await _fileStorageService.MoveFileAsync(songPath, newSongPath, true);
        if (moveFileResult.IsError)
        {
            return moveFileResult;
        }
        return Result.Ok();
    }
}
