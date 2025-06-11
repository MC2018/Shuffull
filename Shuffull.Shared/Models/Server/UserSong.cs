using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;

namespace Shuffull.Shared.Models.Server
{
    [Serializable]
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

        [Key]
        public User User { get; set; }
        [Key]
        public Song Song { get; set; }
    }
}
