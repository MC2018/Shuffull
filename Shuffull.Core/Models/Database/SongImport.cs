using Nut.Results;
using Shuffull.Metadata.Enums;
using Shuffull.Core.Models.Enums;
using System.ComponentModel.DataAnnotations;

namespace Shuffull.Core.Models.Database;

public class SongImport
{
    [Key]
    public string SongImportId { get; set; }
    [Required]
    public string Name { get; set; }
    [Required]
    public string ImportFolder { get; set; }
    [Required]
    public string FileType { get; set; }
    [Required]
    public string UserId { get; set; }
    [Required]
    public SongImportState State { get; private set; } = SongImportState.ReadyForImporting;
    [Required]
    public ExternalSource ExternalSource { get; set; }
    public string? PlaylistId { get; set; }
    public string? ExternalSongId { get; set; }
    public string? ExternalPlaylistId { get; set; }
    public string? SongId { get; set; }
    /// <summary>
    /// When set, this import REPLACES the named Shuffull song in place (re-sourced audio for a flagged song)
    /// instead of creating a new one. Carried from SongImportDetails.ReplacesSongId.
    /// </summary>
    public string? ReplacesSongId { get; set; }
    /// <summary>
    /// Serialized <see cref="Shuffull.Metadata.Models.GeneratedSongTags"/> supplied by the external
    /// producer at ingest. Null when no tags were provided, in which case Shuffull
    /// generates them itself during import.
    /// </summary>
    public string? GeneratedTagsJson { get; set; }
    /// <summary>
    /// Serialized <see cref="Shuffull.Metadata.Models.SongLyrics"/> supplied by the external producer.
    /// Null when no lyrics were found / provided.
    /// </summary>
    public string? LyricsJson { get; set; }
    /// <summary>
    /// JSON-serialized ordered artist list supplied by the producer (<c>SongImportDetails.Artists</c>). When
    /// present, the import uses these as the authoritative artists instead of parsing the audio's ID3 tags
    /// (which a producer-side MusicBrainz match can collapse into one credit string). Null for manual uploads /
    /// older payloads, in which case the file's ID3 performers are used.
    /// </summary>
    public string? ArtistsJson { get; set; }
    /// <summary>Best-effort tempo (BPM) from the producer; null when unknown / not provided.</summary>
    public int? Bpm { get; set; }
    /// <summary>Raw measured tempo - the AI's beat-tracking hint, distinct from the resolved <see cref="Bpm"/>. Producer-only.</summary>
    public int? MeasuredBpm { get; set; }
    /// <summary>AI model that produced the generated tags, for a future model-upgrade re-tag pass. Null when self-generated.</summary>
    public string? TagModel { get; set; }
    /// <summary>Audio-shape inputs that grounded the AI energy estimate; persisted so energy can be regenerated without the audio.</summary>
    public double? LoudnessRangeLu { get; set; }
    public double? CrestFactorDb { get; set; }
    public double? OnsetsPerSecond { get; set; }
    /// <summary>Authoritative original release year (reliable MusicBrainz match); carried through to the Song.</summary>
    public int? OriginalReleaseYear { get; set; }
    /// <summary>Exploratory ("audition") import: skip AI tagging; persist the Song as provisional.</summary>
    public bool Exploratory { get; set; }
    /// <summary>Whether the user liked the song on the external source; mapped to a Like on import.</summary>
    public bool MarkAsLiked { get; set; }
    /// <summary>Name of the target playlist; used to auto-create it (attached to the user) if it doesn't exist.</summary>
    public string? TargetPlaylistName { get; set; }
    [Required]
    public DateTime LastUpdatedAt { get; set; }

    public Song? Song { get; set; }

    public string FileName => $"{SongImportId}{FileType}";
    public string GetImportFolderPath(string songImportDirectory) => Path.Combine(songImportDirectory, ImportFolder);
    public string GetFilePath(string songImportDirectory) => Path.Combine(songImportDirectory, ImportFolder, FileName);

    public Result SetState(SongImportState state)
    {
        if (State != SongImportState.ReadyForImporting)
        {
            return Result.Error("Cannot set state from current state.");
        }

        State = state;
        LastUpdatedAt = DateTime.UtcNow;
        return Result.Ok();
    }
}
