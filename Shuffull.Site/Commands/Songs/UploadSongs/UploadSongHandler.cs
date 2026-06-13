using Nut.Results;
using Shuffull.Shared.Tools;
using Shuffull.Site.Configuration;
using Shuffull.Site.Models.Database;
using Shuffull.Site.Models.Enums;
using Shuffull.Site.Services;
using Shuffull.Site.Services.FileStorage;
using System.Text.RegularExpressions;

namespace Shuffull.Site.Commands.Songs.UploadSongs;

public partial class UploadSongHandler(IServiceProvider services)
{
    public async Task<Result> Handle(UploadSongRequest request)
    {
        // The controller injects the request-scoped IServiceProvider, so ShuffullContext et al.
        // can be resolved directly (no new scope needed).
        var context = services.GetRequiredService<ShuffullContext>();
        var fileStorageService = services.GetRequiredService<IFileStorageService>();
        // ShuffullFilesConfiguration isn't registered in DI; bind it from IConfiguration like the
        // import services do.
        var configuration = services.GetRequiredService<IConfiguration>();
        var fileConfig = configuration.GetSection(ShuffullFilesConfiguration.FilesConfigurationSection).Get<ShuffullFilesConfiguration>()
            ?? throw new InvalidOperationException("Files configuration is not set.");

        var user = context.Users.Where(x => x.Username == request.Username).FirstOrDefault();
        if (user == null)
        {
            return Result.Error("User not found.");
        }

        if (request.Files.Any(x => !SupportedFileTypes().IsMatch(x.FileName)))
        {
            return Result.Error(new NotSupportedException("Only mp3 and wav files are allowed."));
        }

        // Create the target playlist by name if one was provided and doesn't already exist.
        Playlist? playlist = null;
        if (!string.IsNullOrEmpty(request.PlaylistName))
        {
            playlist = context.Playlists.Where(x => x.Name == request.PlaylistName).FirstOrDefault();
            if (playlist == null)
            {
                playlist = new Playlist
                {
                    PlaylistId = IdGenerator.Generate(),
                    UserId = user.UserId,
                    Name = request.PlaylistName,
                    CurrentSongId = null,
                    PercentUntilReplayable = 0.9m
                };
                context.Playlists.Add(playlist);
            }
        }

        // Drop each uploaded file into the SongImport pipeline and queue a SongImport row for
        // SongImportService to pick up (State defaults to ReadyForImporting). Mirrors
        // ExternalSongImporterService, minus the external-source metadata: a manual upload has no
        // external song/playlist id, so the YouTube re-fetch in SongImportService is skipped while
        // AI tag generation still runs off the file's own tags.
        foreach (var file in request.Files)
        {
            var songImport = new SongImport
            {
                SongImportId = IdGenerator.Generate(),
                Name = Path.GetFileNameWithoutExtension(file.FileName),
                ImportFolder = IdGenerator.Generate(),
                FileType = Path.GetExtension(file.FileName).ToLowerInvariant(),
                UserId = user.UserId,
                PlaylistId = playlist?.PlaylistId,
                ExternalSource = ExternalSource.Manual,
                ExternalSongId = null,
                ExternalPlaylistId = null,
                LastUpdatedAt = DateTime.UtcNow,
            };

            var filePath = songImport.GetFilePath(fileConfig.SongImportDirectory);
            using var stream = file.OpenReadStream();
            var uploadFileResult = await fileStorageService.UploadFileAsync(filePath, stream, true);
            if (uploadFileResult.IsError)
            {
                return uploadFileResult;
            }

            context.SongImports.Add(songImport);
        }

        await context.SaveChangesAsync();
        return Result.Ok();
    }

    [GeneratedRegex("\\.(mp3|wav)$")]
    private static partial Regex SupportedFileTypes();
}
