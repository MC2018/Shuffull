using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shuffull.Core.Models.Database
{
    [Index(nameof(Name))]
    [Index(nameof(Version))]
    public class Song
    {
        [Key]
        public string SongId { get; set; }
        [Required]
        public string FileExtension { get; set; } = string.Empty;
        [Required]
        public string FileHash { get; set; } = string.Empty;
        [Required]
        public string Name { get; set; } = string.Empty;
        public string? ExternalSongId { get; set; }

        // Enrichment supplied by the external producer via the import contract; null when not provided.
        // Lyrics: SyncedLyrics is LRC text, already trim-shifted by the producer for YT-Music-sourced lyrics
        // (the app may add a manual nudge); PlainLyrics is the untimed fallback; LyricsInstrumental marks
        // a no-lyrics-by-design track. Bpm is a best-effort tempo estimate.
        public string? SyncedLyrics { get; set; }
        public string? PlainLyrics { get; set; }
        public bool LyricsInstrumental { get; set; }
        public string? LyricsSource { get; set; }
        public int? Bpm { get; set; }
        // Best-effort 1-10 perceived intensity/drive score from the producer's AI (weighs BPM but not purely it).
        public int? Energy { get; set; }

        // Tag provenance + the raw AI inputs, persisted so the library can be re-tagged with a better model
        // later WITHOUT re-downloading or re-analysing the audio. TagModel = which AI model produced the tags
        // (null when Shuffull self-generated them). MeasuredBpm is the raw beat-tracking hint the AI saw
        // (distinct from the resolved Bpm above); LoudnessRangeLu / CrestFactorDb / OnsetsPerSecond are the
        // objective audio-shape features that grounded the energy estimate. All optional / producer-supplied.
        public string? TagModel { get; set; }
        public int? MeasuredBpm { get; set; }
        public double? LoudnessRangeLu { get; set; }
        public double? CrestFactorDb { get; set; }
        public double? OnsetsPerSecond { get; set; }
        // Authoritative original release year (reliable MusicBrainz match) or null; lets a re-tag re-apply the
        // correct era instead of regenerating an AI guess.
        public int? OriginalReleaseYear { get; set; }

        // Set true when a curator hand-edits this song's metadata. While locked, the import/replacement pipeline
        // (SongImportService.ReplaceInDbAsync) re-sources the AUDIO in place but leaves the metadata
        // (Name/Bpm/Energy/artists/tags) alone, so a later re-source can't silently clobber a human correction.
        public bool MetadataLocked { get; set; }

        // Last-modified timestamp for incremental song sync (mirrors UserSong.Version). Stamped on create and on
        // every mutation (e.g. an in-place replacement), so the app can pull only songs changed since its cursor
        // and refresh its local copy — otherwise an app that already holds a song never re-fetches it.
        [Required]
        public DateTime Version { get; set; }

        public ICollection<PlaylistSong> PlaylistSongs { get; set; }
        public ICollection<UserSong> UserSongs { get; set; }
        public ICollection<SongArtist> SongArtists { get; set; }
        public ICollection<SongTag> SongTags { get; set; }
    }
}
