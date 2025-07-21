using Shuffull.Site.Configuration;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Shuffull.Site.Models.Database;
using Shuffull.Shared.Tools;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using Shuffull.Shared.Enums;
using Shuffull.Site.Services.FileStorage;
using Nut.Results;
using Shuffull.Site.Services.AI;
using Shuffull.Site.Tools;
using Shuffull.Site.Tools.SongParsing;
using Tag = Shuffull.Site.Models.Database.Tag;
using Shuffull.Site.Models.Enums;
using FluentAssertions.Common;
using System.Text.RegularExpressions;
using Shuffull.Site.Models.Files;
using Shuffull.Site.Models.AI;

namespace Shuffull.Site.Services;

/// <summary>
/// Handles logic regarding the downloading, file management, and database importing of songs
/// </summary>
public partial class SongImporterService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ShuffullFilesConfiguration _fileConfig;
    private readonly IFileStorageService _fileStorageService;
    private readonly string[] _mimeImageExtensions = { "image/jpeg", "image/png", "image/gif", "image/bmp" };
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="configuration">Configuration</param>
    /// <param name="services">Service provider</param>
    public SongImporterService(IConfiguration configuration, IServiceProvider services, ILogger<SongImporterService> logger, IFileStorageService fileStorageService)
    {
        _services = services;
        _fileConfig = configuration.GetSection(ShuffullFilesConfiguration.FilesConfigurationSection).Get<ShuffullFilesConfiguration>() ?? throw new ArgumentNullException(nameof(_fileConfig));
        _fileStorageService = fileStorageService ?? throw new ArgumentNullException(nameof(fileStorageService));
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            var songUpload = await context.SongUploads
                .Where(x => x.State == SongUploadState.ReadyForImporting)
                .FirstOrDefaultAsync(cancellationToken: cancellationToken);

            if (songUpload != null)
            {
                await ImportSongAsync(songUpload, cancellationToken);
            }
            else
            {
                await Task.Delay(_interval, cancellationToken);
            }
        }
    }

    [GeneratedRegex("\\.(mp3|wav)$")]
    private static partial Regex SupportedFileTypes(); // TODO: use

    private async Task<Result> ImportSongAsync(SongUpload songUpload, CancellationToken cancellationToken = default!)
    {
        var songUploadState = SongUploadState.Completed;

        try
        {
            // Download the file bytes and file hash
            var fileBytesResult = await GetFileBytesAsync(songUpload, cancellationToken);
            if (fileBytesResult.IsError)
            {
                return fileBytesResult.PreserveErrorAs();
            }
            var fileBytes = fileBytesResult.Get();
            var fileHash = Hasher.ShaHash(fileBytes).Substring(0, 32);

            // Parse artists from the music file
            var abstraction = new ByteArrayAudioFileAbstraction(songUpload.FileName, fileBytes);
            var musicFile = TagLib.File.Create(abstraction);
            var parseArtistsResult = await ParseArtistsAsync(songUpload, musicFile, cancellationToken);
            if (parseArtistsResult.IsError)
            {
                return parseArtistsResult.PreserveErrorAs();
            }
            var (existingArtists, newArtists) = parseArtistsResult.Get();

            // Save album art
            var saveAlbumArtResult = await SaveAlbumArtAsync(musicFile, fileHash, cancellationToken);
            if (saveAlbumArtResult.IsError)
            {
                return saveAlbumArtResult;
            }

            //Generate tags for the song
            var artistNames = existingArtists.Select(x => x.Name).Concat(newArtists.Select(x => x.Name)).ToList();
            var generateTagsResult = await GetGeneratedSongTagsAsync(songUpload, artistNames, fileHash, cancellationToken);
            if (generateTagsResult.IsError)
            {
                return generateTagsResult.PreserveErrorAs();
            }
            var (existingTags, newTags) = generateTagsResult.Get();

            // Delete the old file if it exists
            var newPath = Path.Combine(_fileConfig.MusicRootDirectory, $"{fileHash}{Path.GetExtension(songUpload.FileName)}");
            var moveFileResult = await _fileStorageService.MoveFileAsync(songUpload.GetFilePath(_fileConfig.SongImportDirectory), newPath, true, cancellationToken);
            if (moveFileResult.IsError)
            {
                return moveFileResult;
            }

            // Save everything to the db
            var song = new Song
            {
                SongId = IdGenerator.Generate(),
                Name = songUpload.Name,
                FileExtension = Path.GetExtension(songUpload.FileName).ToLowerInvariant(),
                FileHash = fileHash
            };
            var importToDbResult = await ImportToDbAsync(song, existingArtists, newArtists, existingTags, newTags, songUpload.PlaylistId, cancellationToken);
            if (importToDbResult.IsError)
            {
                return importToDbResult;
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            songUploadState = SongUploadState.Failed;
            return Result.Error(ex);
        }
        finally
        {
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            var songUploadDb = context.SongUploads.Find(songUpload.SongUploadId);
            if (songUploadDb != null)
            {
                songUploadDb.SetState(songUploadState);
                context.SongUploads.Update(songUploadDb);
                await context.SaveChangesAsync(cancellationToken);
            }
        }
    }

    private async Task<Result<byte[]>> GetFileBytesAsync(SongUpload songUpload, CancellationToken cancellationToken = default!)
    {
        var fileBytesResult = await _fileStorageService.DownloadFileBytesAsync(songUpload.GetFilePath(_fileConfig.SongImportDirectory), cancellationToken);
        if (fileBytesResult.IsError)
        {
            return fileBytesResult.PreserveErrorAs<byte[]>();
        }
        var fileBytes = fileBytesResult.Get();
        return Result.Ok(fileBytes);
    }

    /// <summary>
    /// Parses artists from the music file
    /// </summary>
    /// <param name="songUpload"></param>
    /// <param name="fileBytes"></param>
    /// <returns>Existing artists and new artists, in that order</returns>
    private async Task<Result<Tuple<List<Artist>, List<Artist>>>> ParseArtistsAsync(SongUpload songUpload, TagLib.File musicFile, CancellationToken cancellationToken = default!)
    {
        using var scope = _services.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
        var performerNames = musicFile.Tag.Performers;
        var existingArtists = new List<Artist>();
        var newArtists = new List<Artist>();

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
            var artist = await context.Artists.Where(a => a.Name == performerName).FirstOrDefaultAsync(cancellationToken);

            if (artist != null)
            {
                existingArtists.Add(artist);
            }
            else
            {
                artist = new Artist()
                {
                    ArtistId = IdGenerator.Generate(),
                    Name = performerName
                };
                newArtists.Add(artist);
            }
        }

        return Result.Ok(Tuple.Create(existingArtists, newArtists));
    }

    private async Task<Result> SaveAlbumArtAsync(TagLib.File musicFile, string fileHash, CancellationToken cancellationToken = default!)
    {
        Image newAlbumArt;
        var resolution = 512;

        if (musicFile.Tag.Pictures.Length > 0)
        {
            var picture = musicFile.Tag.Pictures[0];
            var mimeType = picture.MimeType;

            if (_mimeImageExtensions.Contains(mimeType))
            {
                using var ms = new MemoryStream(picture.Data.Data);
                using var rawAlbumArt = await Image.LoadAsync(ms, cancellationToken);

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

        var outputFilePath = Path.Combine(_fileConfig.AlbumArtDirectory, $"{fileHash}.jpg");
        using var stream = new MemoryStream();
        newAlbumArt.Save(stream, new JpegEncoder());
        stream.Position = 0; // Reset position
        await _fileStorageService.UploadFileAsync(outputFilePath, stream, true, cancellationToken);
        return Result.Ok();
    }

    private async Task<Result<Tuple<List<Tag>, List<Tag>>>> GetGeneratedSongTagsAsync(SongUpload songUpload, List<string> artistNames, string fileHash, CancellationToken cancellationToken = default!)
    {
        using var scope = _services.CreateScope();
        using var dbContext = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
        var aiService = scope.ServiceProvider.GetService<IAIService>();
        var allTags = await dbContext.Tags.AsNoTracking().ToListAsync(cancellationToken);
        var allGenres = await dbContext.Genres
            .AsNoTracking()
            .Include(x => x.GenreRelationsAsMain)
            .ThenInclude(x => x.SubGenre)
            .ToListAsync(cancellationToken); // TODO: specification, you need genre relations as main here
        GeneratedSongTags? generatedSongTags = null;
        List<Tag> existingTags, newTags;

        // Load in existing tags if saved before
        var aiResponseFilePath = Path.Combine(_fileConfig.SavedAiResponsesDirectory, $"{fileHash}.json");
        var generatedSongTagsResult = await _fileStorageService.DownloadSerializableObjectAsync<GeneratedSongTags>(aiResponseFilePath, cancellationToken);
        if (!generatedSongTagsResult.IsError)
        {
            generatedSongTags = generatedSongTagsResult.Get();
        }

        // Run AI service if no tags saved before
        else if (generatedSongTags == null && aiService != null)
        {
            // Main genres
            var allMainGenreNames = allGenres.Where(x => x.IsMain).Select(x => x.Name).ToList();
            var mainGenresRequest = new GenerateMainGenresRequest(songUpload.Name, artistNames, allMainGenreNames);
            var mainGenresResult = await aiService.GenerateMainGenresAsync(mainGenresRequest, cancellationToken);
            if (mainGenresResult.IsError)
            {
                return mainGenresResult.PreserveErrorAs<Tuple<List<Tag>, List<Tag>>>();
            }
            var mainGenresResponse = mainGenresResult.Get();

            // Sub genres
            var selectedMainGenres = allGenres.Where(x => mainGenresResponse.MainGenres.Contains(x.Name)).ToList();
            var allSubGenreNames = selectedMainGenres.SelectMany(x => x.GenreRelationsAsMain).Select(x => x.SubGenre.Name).ToList();
            var subGenresRequest = new GenerateSubGenresRequest(songUpload.Name, artistNames, allSubGenreNames);
            var subGenresResult = await aiService.GenerateSubGenresAsync(subGenresRequest, cancellationToken);
            if (subGenresResult.IsError)
            {
                return subGenresResult.PreserveErrorAs<Tuple<List<Tag>, List<Tag>>>();
            }
            var subGenresResponse = subGenresResult.Get();

            // Other song details
            var otherSongDetailsRequest = new GenerateOtherSongDetailsRequest(songUpload.Name, artistNames);
            var otherSongDetailsResult = await aiService.GenerateOtherSongDetailsAsync(otherSongDetailsRequest, cancellationToken);
            if (otherSongDetailsResult.IsError)
            {
                return otherSongDetailsResult.PreserveErrorAs<Tuple<List<Tag>, List<Tag>>>();
            }
            var otherSongDetailsResponse = otherSongDetailsResult.Get();

            // Save generated tags
            generatedSongTags = new GeneratedSongTags(mainGenresResponse.MainGenres, subGenresResponse.SubGenres, otherSongDetailsResponse.Languages, otherSongDetailsResponse.TimePeriod);
            await _fileStorageService.UploadSerializableObjectAsync(aiResponseFilePath, generatedSongTags, true, cancellationToken);
        }

        // If no tags generated, return empty lists
        if (generatedSongTags == null)
        {
            return Result.Ok(Tuple.Create(new List<Tag>(), new List<Tag>()));
        }

        var generatedTags = generatedSongTags.ToTagList();
        existingTags = allTags.Where(x => generatedTags.Select(y => y.Name).Contains(x.Name)).ToList();
        newTags = generatedTags.Where((x) => !allTags.Select(y => y.Name).Contains(x.Name)).ToList();

        return Result.Ok(Tuple.Create(existingTags, newTags));
    }

    private async Task<Result> ImportToDbAsync(Song song, List<Artist> existingArtists, List<Artist> newArtists, List<Tag> existingTags, List<Tag> newTags, string? playlistId, CancellationToken cancellationToken = default!)
    {
        using var scope = _services.CreateScope();
        using var dbContext = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
        using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Add newly generated items to the db
        dbContext.Songs.Add(song);
        dbContext.Artists.AddRange(newArtists);
        dbContext.Tags.AddRange(newTags);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Add relationships to the db
        var artists = existingArtists.Concat(newArtists).ToList();
        foreach (var artist in artists)
        {
            var songArtist = new SongArtist
            {
                SongArtistId = IdGenerator.Generate(),
                SongId = song.SongId,
                ArtistId = artist.ArtistId
            };
            dbContext.SongArtists.Add(songArtist);
        }

        var tags = existingTags.Concat(newTags).ToList();
        foreach (var tag in tags)
        {
            var songTag = new SongTag
            {
                SongTagId = IdGenerator.Generate(),
                SongId = song.SongId,
                TagId = tag.TagId
            };
            dbContext.SongTags.Add(songTag);
        }

        if (!string.IsNullOrEmpty(playlistId))
        {
            var playlist = await dbContext.Playlists.FindAsync([playlistId], cancellationToken: cancellationToken);
            if (playlist != null)
            {
                var playlistSong = new PlaylistSong
                {
                    PlaylistSongId = IdGenerator.Generate(),
                    PlaylistId = playlist.PlaylistId,
                    SongId = song.SongId
                };
                dbContext.PlaylistSongs.Add(playlistSong);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }
}
