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

        [Key]
        public User User { get; set; }
        [Key]
        public Song Song { get; set; }
    }
}
