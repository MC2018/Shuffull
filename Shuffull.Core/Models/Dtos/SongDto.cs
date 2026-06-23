using Nut.Results;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Models.Dtos;

/// <summary>
/// API-facing projection of a <see cref="Song"/>. Built through the static <see cref="Create"/>
/// factory (mirrors the Sociallite DTO pattern) so mapping/validation lives in one place and can
/// surface failures as a <see cref="Result"/> rather than throwing.
/// </summary>
public record SongDto(
    string SongId,
    string Name,
    string FileExtension,
    string FileHash,
    string? ExternalSongId,
    IReadOnlyList<string> Artists,
    IReadOnlyList<string> Tags,
    // Enrichment from the external producer. SyncedLyrics is LRC text already shifted by LyricsOffsetMs;
    // the app may apply a further manual nudge. Null/0/false when not provided.
    string? SyncedLyrics = null,
    string? PlainLyrics = null,
    bool LyricsInstrumental = false,
    int LyricsOffsetMs = 0,
    string? LyricsSource = null,
    int? Bpm = null)
{
    public static Result<SongDto> Create(Song song)
    {
        if (song is null)
        {
            return Result.Error<SongDto>("Song cannot be null.");
        }

        // Navigation collections are only populated when the query Includes them; treat a missing
        // collection as "none" rather than throwing so the DTO works for lighter-weight queries too.
        var artists = song.SongArtists?
            .Where(sa => sa.Artist is not null)
            .Select(sa => sa.Artist.Name)
            .ToList() ?? new List<string>();

        var tags = song.SongTags?
            .Where(st => st.Tag is not null)
            .Select(st => st.Tag.Name)
            .ToList() ?? new List<string>();

        return Result.Ok(new SongDto(
            SongId: song.SongId,
            Name: song.Name,
            FileExtension: song.FileExtension,
            FileHash: song.FileHash,
            ExternalSongId: song.ExternalSongId,
            Artists: artists,
            Tags: tags,
            SyncedLyrics: song.SyncedLyrics,
            PlainLyrics: song.PlainLyrics,
            LyricsInstrumental: song.LyricsInstrumental,
            LyricsOffsetMs: song.LyricsOffsetMs,
            LyricsSource: song.LyricsSource,
            Bpm: song.Bpm));
    }
}
