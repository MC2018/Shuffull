﻿using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace Shuffull.Site.Models.Database
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

        [Key]
        public User User { get; set; }
        [Key]
        public Song Song { get; set; }
    }
}
