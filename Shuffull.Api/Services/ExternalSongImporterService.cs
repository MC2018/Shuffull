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
        internal TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan MaxLoopErrorDelay = TimeSpan.FromMinutes(5);
        /// <summary>Failed scans in a row; sets the backoff before the next one.</summary>
        private int _consecutiveErrors;

        public ExternalSongImporterService(IServiceProvider services, IConfiguration configuration, ILogger<ExternalSongImporterService> logger)
        {
            _services = services;
            _logger = logger;
            _fileConfig = configuration.GetSection(ShuffullFilesConfiguration.FilesConfigurationSection).Get<ShuffullFilesConfiguration>() ?? throw new Exception("ShuffullFilesConfiguration not configured.");
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var delay = await RunOnceAsync(cancellationToken);
                    await Task.Delay(delay, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }

        /// <summary>
        /// One scan of the drop folder. Returns how long to wait before the next: the poll interval, or a backoff while
        /// scans keep failing. An exception escaping ExecuteAsync stops the whole API (the host's default
        /// BackgroundServiceExceptionBehavior), so a failed scan, whether it comes back as an error or throws, only
        /// pauses the loop.
        /// </summary>
        internal async Task<TimeSpan> RunOnceAsync(CancellationToken cancellationToken)
        {
            Result scan;
            Exception? thrown = null;
            try
            {
                scan = await ImportSongsAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                thrown = ex;
                scan = Result.Error(ex.Message);
            }

            if (scan.IsOk)
            {
                _consecutiveErrors = 0;
                return PollInterval;
            }

            _consecutiveErrors++;
            // The poll interval, doubling, capped at 5 minutes.
            var delay = Backoff.Doubling(PollInterval, _consecutiveErrors, MaxLoopErrorDelay);
            _logger.LogError(thrown, "External song import scan failed ({Count} in a row); retrying in {Delay}: {Error}",
                _consecutiveErrors, delay, scan.GetError());
            return delay;
        }

        /// <summary>Stages every import in the drop folder. An error means the folder couldn't be listed; a file that
        /// fails to import is logged and left for the next scan.</summary>
        private async Task<Result> ImportSongsAsync(CancellationToken cancellationToken)
        {
            using var scope = _services.CreateScope();
            var fileStorageService = scope.ServiceProvider.GetRequiredService<IFileStorageService>();

            var getFilesResult = await fileStorageService.GetFilesAsync(_fileConfig.ExternalSongImportDirectory, cancellationToken: cancellationToken);
            if (getFilesResult.IsError)
            {
                return Result.Error($"Error getting files for external song import: {getFilesResult.GetError()}");
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
            return Result.Ok();
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
