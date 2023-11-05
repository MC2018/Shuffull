using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using Shuffull.Shared.Networking.Models.Server;

namespace Shuffull.Shared.Networking.Models
{
    [Serializable]
    public class RecentlyPlayedSong
    {
        [Key]
        public string RecentlyPlayedSongGuid { get; set; }
        [Required]
        public long SongId { get; set; }
        public int? TimestampSeconds { get; set; }
        [Required]
        public DateTime LastPlayed { get; set; }

        [Key]
        public Song Song { get; set; }
    }
}
