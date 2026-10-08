using Nut.Results;

namespace Shuffull.Core.Services;

/// <summary>
/// Deletes a song's stored media (the fileHash-keyed audio file + its album art) from the host's file storage.
/// Lives in Core as an abstraction so code outside the Api host's storage layer can clean up files; today that is
/// the sweep that removes a purged song's media once its tombstone's grace window has passed. The concrete
/// implementation lives in the host.
/// </summary>
public interface ISongMediaStore
{
    /// <summary>
    /// Deletes the audio + album-art files for a song, keyed by its <paramref name="fileHash"/>. Missing files are
    /// ignored (a no-op), so a retry is safe. Returns an error when a file exists but could not be deleted, so the
    /// caller can retry rather than record the media as gone.
    /// </summary>
    Task<Result> DeleteSongMediaAsync(string fileHash, string fileExtension, CancellationToken cancellationToken = default);
}
