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
    // Last-modified timestamp; the app uses it as its incremental song-sync cursor.
    DateTime Version,
    // Enrichment from the external producer. SyncedLyrics is LRC text (already trim-shifted by the producer
    // for YT-Music lyrics); the app may apply a further manual nudge. Null/false when not provided.
    string? SyncedLyrics = null,
    string? PlainLyrics = null,
    bool LyricsInstrumental = false,
    string? LyricsSource = null,
    int? Bpm = null,
    int? Energy = null,
    // True for an un-vetted "audition" song imported from an exploratory source with no AI tags. The app uses
    // this to surface an audition view and to promote-on-keep (a like enqueues a re-tag, which clears this).
    bool Exploratory = false)
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
            Version: song.Version,
            SyncedLyrics: song.SyncedLyrics,
            PlainLyrics: song.PlainLyrics,
            LyricsInstrumental: song.LyricsInstrumental,
            LyricsSource: song.LyricsSource,
            Bpm: song.Bpm,
            Energy: song.Energy,
            Exploratory: song.Exploratory));
    }
}
