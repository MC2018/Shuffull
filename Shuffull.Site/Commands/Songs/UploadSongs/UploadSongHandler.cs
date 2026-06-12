using FluentAssertions.Common;
using Nut.Results;
using Shuffull.Shared.Tools;
using Shuffull.Site.Configuration;
using Shuffull.Site.Models.Database;
using Shuffull.Site.Services;
using Shuffull.Site.Services.FileStorage;
using System.Text.RegularExpressions;

namespace Shuffull.Site.Commands.Songs.UploadSongs;

public partial class UploadSongHandler(IServiceProvider services)
{
    public Task<Result> Handle(UploadSongRequest request)
    {
        // TODO: Manual upload is temporarily disabled. It previously wrote the now-removed SongUpload
        // model. Re-implement it against the new SongImport pipeline -- create SongImport rows (as
        // ExternalSongImporterService does) for SongImportService to process -- before re-enabling.
        // A reference copy of the old SongUpload-based flow is kept in the commented block below.
        return Task.FromResult(Result.Error("Manual song upload is temporarily disabled pending migration to the SongImport pipeline."));
    }

    /*
    public async Task<Result> Handle(UploadSongRequest request)
    {
        using var context = services.GetRequiredService<ShuffullContext>();
        var songImportService = services.GetRequiredService<SongImportService>();
        var fileStorageService = services.GetRequiredService<IFileStorageService>();
        var fileConfig = services.GetRequiredService<ShuffullFilesConfiguration>();
        var user = context.Users.Where(x => x.Username == request.Username).FirstOrDefault(); // TODO: spec file?
        Playlist? playlist = null;

        if (user == null)
        {
            return Result.Error("User not found.");
        }

        if (request.Files.Where(x => SupportedFileTypes().Match(x.FileName).Success == false).Any())
        {
           return Result.Error(new NotSupportedException("Only mp3 and wav files are allowed."));
        }

        // Create playlist if it doesn't exist
        if (!string.IsNullOrEmpty(request.PlaylistName))
        {
            playlist = context.Playlists.Where(x => x.Name == request.PlaylistName).FirstOrDefault();

            if (playlist == null)
            {
                playlist = new Playlist()
                {
                    PlaylistId = Ulid.NewUlid().ToString(),
                    UserId = user.UserId,
                    Name = request.PlaylistName,
                    CurrentSongId = null,
                    PercentUntilReplayable = 0.9m
                };
                context.Playlists.Add(playlist);
            }
        }

        var uploadFolder = Ulid.NewUlid().ToString();

        // Upload files to the import directory
        foreach (var file in request.Files)
        {
            var songUploadId = Ulid.NewUlid().ToString();
            var name = Path.GetFileNameWithoutExtension(file.FileName);
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var songUpload = new SongUpload
            {
                SongUploadId = songUploadId,
                Name = name,
                UploadFolder = uploadFolder,
                UserId = user.UserId,
                PlaylistId = playlist?.PlaylistId
            };
            var filePath = songUpload.GetFilePath(fileConfig.SongImportDirectory);
            using var stream = file.OpenReadStream();
            var uploadFileResult = await fileStorageService.UploadFileAsync(filePath, stream, true);
            if (uploadFileResult.IsError)
            {
                await fileStorageService.DeleteDirectoryAsync(filePath);
                return uploadFileResult;
            }
            context.SongUploads.Add(songUpload);
        }

        await context.SaveChangesAsync();
        return Result.Ok();
    }*/

    [GeneratedRegex("\\.(mp3|wav)$")]
    private static partial Regex SupportedFileTypes();
}
