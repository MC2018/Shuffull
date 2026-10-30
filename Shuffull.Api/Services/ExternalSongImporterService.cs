using FluentAssertions.Common;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Nut.Results;
using Shuffull.Shared.Tools;
using Shuffull.Api.Configuration;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence;
using Shuffull.Core.Tools;
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
        private readonly ILogger<ExternalSongImporterService> _logger;
        private readonly ShuffullFilesConfiguration _fileConfig;
        /// <summary>How long the loop waits between scans of the drop folder. Settable for tests.</summary>
        internal TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(20); // TODO: change to 5 minutes
        private static readonly TimeSpan MaxLoopErrorDelay = TimeSpan.FromMinutes(5);

        public ExternalSongImporterService(IServiceProvider services, IConfiguration configuration, ILogger<ExternalSongImporterService> logger)
        {
            _services = services;
            _logger = logger;
            _fileConfig = configuration.GetSection(ShuffullFilesConfiguration.FilesConfigurationSection).Get<ShuffullFilesConfiguration>() ?? throw new Exception("ShuffullFilesConfiguration not configured.");
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            // An exception escaping ExecuteAsync stops the whole API (the host's default BackgroundServiceExceptionBehavior),
            // so a failed scan (a database or storage blip) must only pause the loop, backing off while it lasts.
            var consecutiveErrors = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                var delay = PollInterval;
                try
                {
                    await ImportSongsAsync(cancellationToken);
                    consecutiveErrors = 0;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    consecutiveErrors++;
                    delay = LoopErrorDelay(consecutiveErrors, PollInterval);
                    _logger.LogError(ex, "External song import scan failed ({Count} in a row); retrying in {Delay}", consecutiveErrors, delay);
                }

                try
                {
                    await Task.Delay(delay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        /// <summary>The loop's pause after its Nth consecutive error: the poll interval, doubling, capped at 5 minutes.</summary>
        internal static TimeSpan LoopErrorDelay(int consecutiveErrors, TimeSpan interval) =>
            Backoff.Doubling(interval, consecutiveErrors, MaxLoopErrorDelay);

        private async Task ImportSongsAsync(CancellationToken cancellationToken)
        {
            using var scope = _services.CreateScope();
            var fileStorageService = scope.ServiceProvider.GetRequiredService<IFileStorageService>();

            var getFilesResult = await fileStorageService.GetFilesAsync(_fileConfig.ExternalSongImportDirectory, cancellationToken: cancellationToken);
            if (getFilesResult.IsError)
            {
                _logger.LogError("Error getting files for external song import: {Error}", getFilesResult.GetError());
                return;
            }
            var filePaths = getFilesResult.Get();

            foreach (var filePath in filePaths)
            {
                var importSongResult = await ImportSongAsync(filePath, cancellationToken);
                if (importSongResult.IsError)
                {
                    _logger.LogError("Error importing song from external import file '{FilePath}': {Error}", filePath, importSongResult.GetError());
                }
            }
        }

        private async Task<Result> ImportSongAsync(string filePath, CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _services.CreateScope();
                var fileStorageService = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
                var intakeService = scope.ServiceProvider.GetRequiredService<SongImportIntakeService>();

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

                var fileBytesResult = await fileStorageService.DownloadFileBytesAsync(songFilePath, cancellationToken);
                if (fileBytesResult.IsError)
                {
                    return Result.Error($"Error downloading song file for external song import: {fileBytesResult.GetError()}");
                }

                // Stage the import through the shared intake path — the same code the HTTP ingestion endpoint uses.
                var createResult = await intakeService.CreateImportAsync(songImportDetails, fileBytesResult.Get(), cancellationToken);
                if (createResult.IsError)
                {
                    return Result.Error(createResult.GetError().Message);
                }

                // The import is staged; remove the source drop files (audio + details json).
                var deleteAudioResult = await fileStorageService.DeleteFileAsync(songFilePath, cancellationToken);
                if (deleteAudioResult.IsError)
                {
                    _logger.LogWarning("Error deleting external song import audio '{FilePath}': {Error}", songFilePath, deleteAudioResult.GetError());
                }
                var deleteFileResult = await fileStorageService.DeleteFileAsync(filePath, cancellationToken);
                if (deleteFileResult.IsError)
                {
                    _logger.LogWarning("Error deleting external song import details file '{FilePath}': {Error}", filePath, deleteFileResult.GetError());
                }

                return Result.Ok();

            }
            catch (Exception ex)
            {
                return Result.Error($"Error importing song from external import: {ex.Message}");
            }
        }
    }
}
