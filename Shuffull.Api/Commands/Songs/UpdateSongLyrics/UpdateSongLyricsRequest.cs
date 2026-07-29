namespace Shuffull.Api.Commands.Songs.UpdateSongLyrics;

/// <summary>
/// Body for the producer's lyrics delivery, keyed by the YouTube video id the song was imported from.
/// Exactly one of <paramref name="SyncedLyrics"/> / <paramref name="PlainLyrics"/> / <paramref name="Instrumental"/>
/// is expected to be meaningful; all three empty is treated as "still nothing found" and rejected, so a
/// producer bug can't blank lyrics the site already holds.
/// </summary>
public record UpdateSongLyricsRequest(
    string ExternalSongId,
    string? SyncedLyrics,
    string? PlainLyrics,
    bool Instrumental,
    string? Source);
