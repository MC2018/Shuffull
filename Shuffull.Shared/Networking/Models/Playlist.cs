﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Shuffull.Shared.Networking.Models
{
    [Serializable]
    public class Playlist
    {
        [Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
        public long PlaylistId { get; set; }
        [Required]
        public long UserId { get; set; }
        [Required]
        public string Name { get; set; }
        [Required]
        public long CurrentSongId { get; set; }
        [Required]
        public decimal PercentUntilReplayable { get; set; }
        [Required]
        public long VersionCounter { get; set; }

        public ICollection<PlaylistSong> PlaylistSongs { get; set; }
    }
}
