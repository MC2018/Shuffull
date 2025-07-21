using Newtonsoft.Json;
using Nut.Results;
using System.IO;

namespace Shuffull.Site.Services.FileStorage;

public class LocalFileStorageService : IFileStorageService
{


    public Task<Result> DeleteFileAsync(string filePath, CancellationToken cancellationToken = default!)
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

    public async Task<Result<byte[]>> DownloadFileBytesAsync(string filePath, CancellationToken cancellationToken = default!)
    {
        try
        {
            var fileBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
            return Result.Ok(fileBytes);
        }
        catch (Exception ex)
        {
            return Result.Error<byte[]>(ex);
        }
    }

    public async Task<Result<string>> DownloadRawTextAsync(string filePath, CancellationToken cancellationToken = default!)
    {
        try
        {
            var fileText = await File.ReadAllTextAsync(filePath, cancellationToken);
            return Result.Ok(fileText);
        }
        catch (Exception ex)
        {
            return Result.Error<string>(ex);
        }
    }

    public async Task<Result<T>> DownloadSerializableObjectAsync<T>(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var fileText = await File.ReadAllTextAsync(filePath, cancellationToken);
            var deserializedData = JsonConvert.DeserializeObject<T>(fileText);
            if (deserializedData == null)
            {
                return Result.Error<T>($"Deserialization failed while downloading a file. Path: {filePath}");
            }

            return Result.Ok(deserializedData);
        }
        catch (Exception ex)
        {
            return Result.Error<T>(ex);
        }
    }

    public async Task<Result> UploadFileAsync(string filePath, Stream stream, bool overwrite, CancellationToken cancellationToken = default!)
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
            using var fileStream = new FileStream(filePath, FileMode.Create);
            await stream.CopyToAsync(fileStream, cancellationToken);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Error(ex);
        }
    }

    public async Task<Result> UploadSerializableObjectAsync(string filePath, object data, bool overwrite, CancellationToken cancellationToken = default!)
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

            var serializedText = JsonConvert.SerializeObject(data);
            if (serializedText == null)
            {
                return Result.Error(new JsonSerializationException($"Serialization while saving an object failed. Path: {filePath}\tData: {data}"));
            }
            await File.WriteAllTextAsync(filePath, serializedText, cancellationToken);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Error(ex);
        }
    }

    public async Task<Result> MoveFileAsync(string sourceFilePath, string destinationFilePath, bool overwrite, CancellationToken cancellationToken = default!)
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
                (await DeleteFileAsync(destinationFilePath, cancellationToken)).ThrowIfError();
            }
            File.Move(sourceFilePath, destinationFilePath);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Error(ex);
        }
    }

    private static Result EnsureDirectoryExists(string filePath)
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

    public Task<Result<List<string>>> GetFilesAsync(string directoryPath, string searchPattern = "*", CancellationToken cancellationToken = default!)
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

    public Task<Result> DeleteDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default!)
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
