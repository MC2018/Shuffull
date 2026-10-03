using Nut.Results;

namespace Shuffull.Core.Services;

/// <summary>
/// Deletes a song's stored media (the fileHash-keyed audio file + its album art) from the host's file storage.
/// Lives in Core as an abstraction so a provider-agnostic feature handler (e.g. purging un-kept audition songs
/// when an exploratory playlist is deleted) can clean up files without depending on the Api host's storage
/// layer. The concrete implementation lives in the host.
/// </summary>
public interface ISongMediaStore
{
    /// <summary>
    /// Best-effort deletion of the audio + album-art files for a song, keyed by its <paramref name="fileHash"/>.
    /// Missing files are ignored (a no-op). Intended to run after the DB rows are already gone, so a storage
    /// hiccup only leaves an orphaned file rather than failing the operation.
    /// </summary>
    Task<Result> DeleteSongMediaAsync(string fileHash, string fileExtension, CancellationToken cancellationToken = default);
}
