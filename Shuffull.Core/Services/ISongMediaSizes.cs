namespace Shuffull.Core.Services;

/// <summary>
/// Reads how much space a song's stored media takes: its fileHash-keyed audio file and its album art. Lives in Core as
/// an abstraction so the auto-download sizes query (Shuffull#150) can stat files without knowing where the host keeps
/// them. The concrete implementation lives in the host.
///
/// <para>Sizes are read from the files on each call, never stored on <c>Song</c>. Files are named by their content
/// hash, so a re-source writes a new file under a new name and a size read now is never stale. Reading them costs one
/// stat per file. Measured 2026-11-23: warm, 13,000 stats took 35 ms on a dev machine. Cold is not pinned down: over
/// HTTP to prod (6,501 songs, HDD pool) a file's first read was no slower than its second (p50 391 ms vs 393 ms), but
/// that ~390 ms network floor would hide a few ms per stat, so the first call after a reboot may be slower. If that
/// ever matters, cache by file hash: a hash's size never changes, so such a cache can't go stale.</para>
/// </summary>
public interface ISongMediaSizes
{
    /// <summary>
    /// The song's media sizes, or null when its audio file is missing. A missing album art counts as 0 bytes, since
    /// there is nothing to download for it.
    /// </summary>
    SongMediaSize? GetSongMediaSize(string fileHash, string fileExtension);
}

/// <param name="AudioBytes">Size of the audio file served at <c>/music/{FileHash}{FileExtension}</c>.</param>
/// <param name="ArtBytes">Size of the album art served at <c>/albumart/{FileHash}.jpg</c>; 0 when there is none.</param>
public record SongMediaSize(long AudioBytes, long ArtBytes);
