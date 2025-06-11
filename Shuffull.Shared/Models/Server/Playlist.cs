using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Shuffull.Shared.Models.Server
{
    [Serializable]
    public class Playlist
    {
        [Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
        public string PlaylistId { get; set; }
        [Required]
        public string UserId { get; set; }
        [Required]
        public string Name { get; set; }
        [Required]
        public string CurrentSongId { get; set; }
        [Required]
        public decimal PercentUntilReplayable { get; set; }
        [Required]
        public DateTime Version { get; set; }

        [Key]
        public User User { get; set; }
        public ICollection<PlaylistSong> PlaylistSongs { get; set; }
    }
}
