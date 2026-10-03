using Nut.Results;

namespace Shuffull.Api.Services.FileStorage;

public interface IFileStorageService
{
    /// <summary>
    /// Uploads a file to the storage.
    /// </summary>
    /// <param name="filePath">The path where the file will be stored.</param>
    /// <param name="stream">The content of the file to be stored.</param>
    /// <param name="overwrite">Indicates whether to overwrite the file if it already exists at the destination.</param>
    /// <returns>True if the upload was successful, otherwise false.</returns>
    Task<Result> UploadFileAsync(string filePath, Stream stream, bool overwrite, CancellationToken cancellationToken = default!);

    /// <summary>
    /// Uploads a serializable object to the storage.
    /// </summary>
    /// <param name="filePath">The path where the file will be stored.</param>
    /// <param name="data">The serializable data to be stored.</param>
    /// <param name="overwrite">Indicates whether to overwrite the file if it already exists at the destination.</param>
    /// <returns>True if the upload was successful, otherwise false.</returns>
    Task<Result> UploadSerializableObjectAsync(string filePath, object data, bool overwrite, CancellationToken cancellationToken = default!);

    /// <summary>
    /// Downloads a file from the storage.
    /// </summary>
    /// <param name="filePath">The path of the file to be downloaded.</param>
    /// <returns>A byte array of the file.</returns>
    Task<Result<byte[]>> DownloadFileBytesAsync(string filePath, CancellationToken cancellationToken = default!);

    /// <summary>
    /// Downloads a file as a serializable object from the storage.
    /// </summary>
    /// <param name="filePath">The path of the file to be downloaded.</param>
    /// <returns>A serialized object.</returns>
    Task<Result<T>> DownloadSerializableObjectAsync<T>(string filePath, CancellationToken cancellationToken = default!);

    /// <summary>
    /// Deletes a file from the storage.
    /// </summary>
    /// <param name="filePath">The path of the file to be deleted.</param>
    /// <returns>True if the deletion was successful, otherwise false.</returns>
    Task<Result> DeleteFileAsync(string filePath, CancellationToken cancellationToken = default!);

    /// <summary>
    /// Moves a file from one location to another (file name included).
    /// </summary>
    /// <param name="sourceFilePath"></param>
    /// <param name="destinationFilePath"></param>
    /// <param name="overwrite">Indicates whether to overwrite the file if it already exists at the destination.</param>
    /// <returns></returns>
    Task<Result> MoveFileAsync(string sourceFilePath, string destinationFilePath, bool overwrite, CancellationToken cancellationToken = default!);

    /// <summary>
    /// Gets a list of file paths, non-recursively.
    /// </summary>
    /// <param name="directoryPath"></param>
    /// <param name="searchPattern"></param>
    /// <returns></returns>
    Task<Result<List<string>>> GetFilesAsync(string directoryPath, string searchPattern = "*", CancellationToken cancellationToken = default!);

    /// <summary>
    /// Deletes a directory and all its contents.
    /// </summary>
    /// <param name="directoryPath"></param>
    /// <returns></returns>
    Task<Result> DeleteDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default!);
}
