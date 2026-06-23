using FluentAssertions.Common;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Nut.Results;
using Shuffull.Shared.Tools;
using Shuffull.Api.Configuration;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence;
using Shuffull.Metadata.Contracts;
using Shuffull.Metadata.Enums;
using Shuffull.Api.Services.FileStorage;
using Shuffull.Api.Tools.SongParsing;
using System.Threading;
using TagLib;

namespace Shuffull.Api.Services
{
    public class ExternalSongImporterService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly SongImportService _songImporter;
        public static ShuffullFilesConfiguration _fileConfig { get; private set; } // TODO: bad
        private readonly TimeSpan _interval = TimeSpan.FromSeconds(20); // TODO: change to 5 minutes

        public ExternalSongImporterService(IServiceProvider services, IConfiguration configuration)
        {
            _services = services;
            _fileConfig = configuration.GetSection(ShuffullFilesConfiguration.FilesConfigurationSection).Get<ShuffullFilesConfiguration>() ?? throw new Exception("ShuffullFilesConfiguration not configured.");
            // _songImporter = _services.GetRequiredService<SongImportService>();
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await ImportSongsAsync(cancellationToken);
                await Task.Delay(_interval, cancellationToken);
            }
        }

        private async Task ImportSongsAsync(CancellationToken cancellationToken)
        {
            using var scope = _services.CreateScope();
            var fileStorageService = scope.ServiceProvider.GetRequiredService<IFileStorageService>();

            var getFilesResult = await fileStorageService.GetFilesAsync(_fileConfig.ExternalSongImportDirectory, cancellationToken: cancellationToken);
            if (getFilesResult.IsError)
            {
                Console.WriteLine($"Error getting files for external song import: {getFilesResult.GetError()}");
                return;
            }
            var filePaths = getFilesResult.Get();

            foreach (var filePath in filePaths)
            {
                var importSongResult = await ImportSongAsync(filePath, cancellationToken);
                if (importSongResult.IsError)
                {
                    Console.WriteLine($"Error importing song from external import file '{filePath}': {importSongResult.GetError()}");
                }
            }
        }

        private async Task<Result> ImportSongAsync(string filePath, CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _services.CreateScope();
                using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
                var fileStorageService = scope.ServiceProvider.GetRequiredService<IFileStorageService>();

                if (!filePath.ToLowerInvariant().EndsWith(".json"))
                {
                    return Result.Ok();
                    //return Result.Error("File is not a JSON file.");
                }

                var getSongImportDetailsResult = await fileStorageService.DownloadSerializableObjectAsync<SongImportDetails>(filePath, cancellationToken);
                if (getSongImportDetailsResult.IsError)
                {
                    return Result.Error($"Error downloading song import details: {getSongImportDetailsResult.GetError()}");
                }
                var songImportDetails = getSongImportDetailsResult.Get();

                var songFilePath = Path.Combine(_fileConfig.ExternalSongImportDirectory, $"{songImportDetails.ExternalSongId}{songImportDetails.FileExtension}");

                var uploadFolder = IdGenerator.Generate();
                var songImportId = IdGenerator.Generate();

                var fileBytesResult = await fileStorageService.DownloadFileBytesAsync(songFilePath, cancellationToken);
                if (fileBytesResult.IsError)
                {
                    return Result.Error($"Error downloading song file for external song import: {fileBytesResult.GetError()}");
                }
                var fileBytes = fileBytesResult.Get();

                var abstraction = new ByteArrayAudioFileAbstraction(songFilePath, fileBytes);
                var musicFile = TagLib.File.Create(abstraction);
                var songName = string.IsNullOrWhiteSpace(musicFile.Tag.Title) ? songImportDetails.ExternalSongId : musicFile.Tag.Title;
                var songImport = new SongImport
                {
                    SongImportId = songImportId,
                    Name = songName,
                    ImportFolder = uploadFolder,
                    UserId = songImportDetails.TargetUserId,
                    PlaylistId = songImportDetails.TargetPlaylistId,
                    FileType = songImportDetails.FileExtension,
                    ExternalSongId = songImportDetails.ExternalSongId,
                    ExternalPlaylistId = songImportDetails.ExternalPlaylistId,
                    ExternalSource = songImportDetails.ExternalSource,
                    // Carry the external producer's inferred genre tags through so SongImportService can use
                    // them instead of re-running its own AI + YouTube re-fetch. Null for manual uploads.
                    GeneratedTagsJson = songImportDetails.GeneratedTags is null
                        ? null
                        : JsonConvert.SerializeObject(songImportDetails.GeneratedTags),
                    // Producer-supplied lyrics + tempo, carried for SongImportService to persist on the Song.
                    LyricsJson = songImportDetails.Lyrics is null
                        ? null
                        : JsonConvert.SerializeObject(songImportDetails.Lyrics),
                    Bpm = songImportDetails.Bpm,
                    LastUpdatedAt = DateTime.UtcNow,
                };
                var newSongFilePath = songImport.GetFilePath(_fileConfig.SongImportDirectory);
                context.SongImports.Add(songImport);

                var moveFileResult = await fileStorageService.MoveFileAsync(songFilePath, newSongFilePath, true, cancellationToken);
                if (moveFileResult.IsError)
                {
                    return Result.Error($"Error moving song file for external song import: {moveFileResult.GetError()}");
                }

                var deleteFileResult = await fileStorageService.DeleteFileAsync(filePath, cancellationToken);
                if (deleteFileResult.IsError)
                {
                    Console.WriteLine($"Warning: Error deleting external song import details file '{filePath}': {deleteFileResult.GetError()}");
                }

                await context.SaveChangesAsync(cancellationToken);
                return Result.Ok();

            }
            catch (Exception ex)
            {
                return Result.Error($"Error importing song from external import: {ex.Message}");
            }
        }
    }
}
