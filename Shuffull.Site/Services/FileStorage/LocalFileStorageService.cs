using Nut.Results;
using System.IO;

namespace Shuffull.Site.Services.FileStorage;

public class LocalFileStorageService : IFileStorageService
{
    public Task<Result> DeleteFileAsync(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
            return Task.FromResult(Result.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result.Error(ex));
        }
    }

    public async Task<Result<byte[]>> DownloadFileBytesAsync(string filePath)
    {
        try
        {
            var fileBytes = await File.ReadAllBytesAsync(filePath);
            return Result.Ok(fileBytes);
        }
        catch (Exception ex)
        {
            return Result.Error<byte[]>(ex);
        }
    }

    public async Task<Result> UploadFileAsync(string filePath, IFormFile formFile, bool overwrite)
    {
        try
        {
            EnsureDirectoryExists(filePath).ThrowIfError();
            if (File.Exists(filePath))
            {
                if (!overwrite)
                {
                    return Result.Error("Destination file already exists and overwriting is not allowed.");
                }
                File.Delete(filePath);
            }
            using var stream = new FileStream(filePath, FileMode.Create);
            await formFile.CopyToAsync(stream);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Error(ex);
        }
    }

    public async Task<Result> MoveFileAsync(string sourceFilePath, string destinationFilePath, bool overwrite)
    {
        try
        {
            if (!File.Exists(sourceFilePath))
            {
                return Result.Error("Source file does not exist.");
            }

            EnsureDirectoryExists(destinationFilePath).ThrowIfError();
            if (File.Exists(destinationFilePath))
            {
                if (!overwrite)
                {
                    return Result.Error("Destination file already exists and overwriting is not allowed.");
                }
                (await DeleteFileAsync(destinationFilePath)).ThrowIfError();
            }
            File.Move(sourceFilePath, destinationFilePath);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Error(ex);
        }
    }

    private Result EnsureDirectoryExists(string filePath)
    {
        try
        {
            var directoryName = Path.GetDirectoryName(filePath);
            if (directoryName != null && !Directory.Exists(directoryName))
            {
                Directory.CreateDirectory(directoryName);
            }
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Error(ex);
        }
    }

    public Task<Result<List<string>>> GetFilesAsync(string directoryPath, string searchPattern = "*")
    {
        try
        {
            var list = Directory.GetFiles(directoryPath, searchPattern).OrderBy(x => x).ToList();
            return Task.FromResult(Result.Ok(list));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result.Error<List<string>>(ex));
        }
    }

    public Task<Result> DeleteDirectoryAsync(string directoryPath)
    {
        try
        {
            var directoryInfo = new DirectoryInfo(directoryPath);

            foreach (var directory in directoryInfo.GetDirectories())
            {
                directory.Delete(true);
            }

            foreach (var file in directoryInfo.GetFiles())
            {
                file.Delete();
            }

            Directory.Delete(directoryPath);
            return Task.FromResult(Result.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result.Error(ex));
        }
    }
}
