using Microsoft.Extensions.Configuration;
using Nut.Results;
using Shuffull.Api.Configuration;
using Shuffull.Api.Services.FileStorage;
using Shuffull.Core.Services;

namespace Shuffull.Api.Services;

/// <summary>
/// Host-side <see cref="ISongMediaStore"/>: deletes the fileHash-keyed audio file (under
/// <see cref="ShuffullFilesConfiguration.MusicRootDirectory"/>) and its album art
/// (<see cref="ShuffullFilesConfiguration.AlbumArtDirectory"/>/&lt;hash&gt;.jpg). Mirrors the file layout the
/// import writes and the replacement flow's own cleanup. A missing file is a no-op, so a retry is safe. A storage
/// failure is logged and returned as an error (after still trying the other file) so the caller can retry.
/// </summary>
public class SongMediaStore : ISongMediaStore
{
    private readonly IFileStorageService _fileStorageService;
    private readonly ShuffullFilesConfiguration _fileConfig;
    private readonly ILogger<SongMediaStore> _logger;

    public SongMediaStore(IConfiguration configuration, IFileStorageService fileStorageService, ILogger<SongMediaStore> logger)
    {
        _fileConfig = configuration.GetSection(ShuffullFilesConfiguration.FilesConfigurationSection).Get<ShuffullFilesConfiguration>()
            ?? throw new ArgumentNullException(nameof(configuration), "Files configuration section is missing.");
        _fileStorageService = fileStorageService ?? throw new ArgumentNullException(nameof(fileStorageService));
        _logger = logger;
    }

    public async Task<Result> DeleteSongMediaAsync(string fileHash, string fileExtension, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileHash))
        {
            return Result.Ok();
        }

        var paths = new[]
        {
            Path.Combine(_fileConfig.MusicRootDirectory, $"{fileHash}{fileExtension}"),
            Path.Combine(_fileConfig.AlbumArtDirectory, $"{fileHash}.jpg"),
        };

        var failedPaths = new List<string>();
        foreach (var path in paths)
        {
            var deleteResult = await _fileStorageService.DeleteFileAsync(path, cancellationToken);
            if (deleteResult.IsError)
            {
                _logger.LogWarning("Failed to delete purged song file '{Path}': {Error}", path, deleteResult.GetError().Message);
                failedPaths.Add(path);
            }
        }

        return failedPaths.Count == 0
            ? Result.Ok()
            : Result.Error($"Could not delete {string.Join(", ", failedPaths)}.");
    }
}
