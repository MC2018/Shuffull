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

        public ICollection<PlaylistSong> PlaylistSongs { get; set; }
        public ICollection<UserSong> UserSongs { get; set; }
        public ICollection<SongArtist> SongArtists { get; set; }
        public ICollection<SongTag> SongTags { get; set; }
    }
}
