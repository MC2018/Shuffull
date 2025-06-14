﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using Shuffull.Shared.Models.Server;

namespace Shuffull.Shared.Models
{
    [Serializable]
    public class RecentlyPlayedSong
    {
        [Key]
        public string RecentlyPlayedSongId { get; set; }
        [Required]
        public string SongId { get; set; }
        public int? TimestampSeconds { get; set; }
        [Required]
        public DateTime LastPlayed { get; set; }

        [Key]
        public Song Song { get; set; }
    }
}
