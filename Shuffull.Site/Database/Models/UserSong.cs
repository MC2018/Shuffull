using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace Shuffull.Site.Database.Models
{
    [Index(nameof(UserId)), Index(nameof(SongId)), Index(nameof(LastPlayed))]
    public class UserSong
    {
        [Required]
        public long UserId { get; set; }
        [Required]
        public long SongId { get; set; }
        [Required]
        public DateTime LastPlayed { get; set; }
        [Required]
        public DateTime Version { get; set; }

        [Key]
        public User User { get; set; }
        [Key]
        public Song Song { get; set; }
    }
}
