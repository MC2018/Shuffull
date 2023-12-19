using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;

namespace Shuffull.Shared.Networking.Models.Server
{
    [Serializable]
    public class UserSong
    {
        [Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
        public long UserSongId { get; set; }
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
