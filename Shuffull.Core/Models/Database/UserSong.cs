using Microsoft.EntityFrameworkCore;
using Shuffull.Core.Models.Enums;
using System.ComponentModel.DataAnnotations;

namespace Shuffull.Core.Models.Database
{
    [Index(nameof(UserId)), Index(nameof(SongId)), Index(nameof(LastPlayed))]
    public class UserSong
    {
        [Required]
        public string UserId { get; set; }
        [Required]
        public string SongId { get; set; }
        [Required]
        public DateTime LastPlayed { get; set; }
        [Required]
        public DateTime Version { get; set; }
        /// <summary>The user's sentiment toward this song; Neutral by default.</summary>
        [Required]
        public LikeStatus LikeStatus { get; set; } = LikeStatus.Neutral;
        /// <summary>
        /// When this user first decided to keep the song, UTC: a Keep (<c>/songs/keep</c>), a Like or Love, or an import
        /// that was liked or wasn't an audition. Set once, in the same commit as that decision, and never cleared: an
        /// unlike later doesn't undo the Keep. Null if none of those has happened since this was recorded (Shuffull#101;
        /// there is no backfill). The recap's keep rate reads it; it isn't sent to clients.
        /// </summary>
        public DateTime? KeptAt { get; set; }

        [Key]
        public User User { get; set; }
        [Key]
        public Song Song { get; set; }
    }
}
