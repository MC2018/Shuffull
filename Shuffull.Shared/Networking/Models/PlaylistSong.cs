﻿using System;
using System.ComponentModel.DataAnnotations;

namespace Shuffull.Shared.Networking.Models
{
    [Serializable]
    public class PlaylistSong
    {
        [Key]
        public long PlaylistSongId { get; set; }
        [Required]
        public long PlaylistId { get; set; }
        [Required]
        public long SongId { get; set; }
        [Required]
        public DateTime LastAddedToQueue { get; set; }
        [Required]
        public DateTime LastPlayed { get; set; }
        [Required]
        public bool InQueue { get; set; }

        [Key]
        public Playlist Playlist { get; set; }
        [Key]
        public Song Song { get; set; }
    }
}