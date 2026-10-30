using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Linq.Expressions;
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
        // 1-10 HEAVINESS score from the producer's AI: one absolute scale across genres (1 = ambient, 10 =
        // punishing). Called Energy until shuffull-workspace#50.
        public int? Heaviness { get; set; }

        // Tag provenance + the raw AI inputs, persisted so the library can be re-tagged with a better model
        // later WITHOUT re-downloading or re-analysing the audio. TagModel = which AI model produced the tags
        // (null when Shuffull self-generated them). MeasuredBpm is the raw beat-tracking hint the AI saw
        // (distinct from the resolved Bpm above); LoudnessRangeLu / CrestFactorDb / OnsetsPerSecond are the
        // objective audio-shape features that grounded the heaviness estimate. All optional / producer-supplied.
        public string? TagModel { get; set; }
        // The prompt revision (Shuffull.Metadata TagRevision) the tags came from. 0 = before revisions existed.
        // A tagged song behind TagRevision.Current is re-scored once by the producer queue (GetPendingTagSongs).
        // The site's own enrichment stamps one behind on purpose (SongEnrichmentService.SiteTagRevision): it has no
        // audio grit or density, so the producer re-scores what it tags (Shuffull#73).
        public int TagRevision { get; set; }
        public int? MeasuredBpm { get; set; }
        public double? LoudnessRangeLu { get; set; }
        public double? CrestFactorDb { get; set; }
        public double? OnsetsPerSecond { get; set; }
        // Authoritative original release year (reliable MusicBrainz match) or null; lets a re-tag re-apply the
        // correct era instead of regenerating an AI guess.
        public int? OriginalReleaseYear { get; set; }
        // Where the song's hook begins, as a percent of its length (0-100): the app's audition start point. From the
        // producer's import; null = unknown, and the app then picks its own default. Nobody edits it by hand, so a
        // re-source with a value replaces it even on a MetadataLocked song; one without keeps the old value, unless it
        // moves the song to a different video.
        public double? HookStartPercent { get; set; }

        // Provisional "audition" song: imported from an exploratory source with NO AI tags, awaiting the user's
        // keep decision. While true it is excluded from the model-upgrade sweep (don't spend AI on un-vetted
        // songs) and can be purged when its exploratory playlist is deleted. A re-tag (promote-on-keep) clears
        // this and gives it real tags.
        public bool Exploratory { get; set; }

        // Set true when a curator hand-edits this song's metadata. While locked, the import/replacement pipeline
        // (SongImportService.ReplaceInDbAsync) re-sources the AUDIO in place but leaves the metadata
        // (Name/Bpm/Heaviness/artists/tags) alone, so a later re-source can't silently clobber a human correction.
        public bool MetadataLocked { get; set; }

        // Producer tagging queue backoff (GetPendingTagSongs). TagAttempts counts hand-outs since tags were last
        // written, and the queue skips the song until NextTagAttemptAt, so a song that can't be tagged backs off
        // instead of holding the head of the queue forever (Shuffull#40). Never touches Version: clients don't
        // sync these, and bumping it would make them re-fetch the song every cycle.
        public int TagAttempts { get; set; }
        public DateTime? NextTagAttemptAt { get; set; }

        /// <summary>Call wherever TagModel is written: freshly tagged, the song owes the queue nothing.</summary>
        public void ResetTagAttempts()
        {
            TagAttempts = 0;
            NextTagAttemptAt = null;
        }

        /// <summary>
        /// A song's place in the producer tagging queue, oldest first: when it was last changed (<see cref="Version"/>),
        /// or, for a song the producer reported held for an AI batch (HoldTagSongs), <paramref name="heldYieldFor"/>
        /// after it is due again. So a held song rejoins the line at the back, behind work asked for up to that long
        /// after its hold, and ahead of anything newer.
        ///
        /// <para>Only a hold report leaves a song at no attempts with a due time set: a hand-out always counts an
        /// attempt, and writing tags clears both (<see cref="ResetTagAttempts"/>). A new writer that sets a due time
        /// without counting an attempt would make its songs sort as held.</para>
        /// </summary>
        public static Expression<Func<Song, DateTime>> TagQueuePlace(TimeSpan heldYieldFor)
        {
            var seconds = heldYieldFor.TotalSeconds;
            return s => s.TagAttempts == 0 && s.NextTagAttemptAt != null
                ? s.NextTagAttemptAt.Value.AddSeconds(seconds)
                : s.Version;
        }

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
