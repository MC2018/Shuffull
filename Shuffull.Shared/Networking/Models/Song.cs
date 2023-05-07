using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Shuffull.Shared.Networking.Models
{
    [Serializable]
    public class Song
    {
        [Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
        public long SongId { get; set; }
        [Required]
        public string Directory { get; set; }
        [Required]
        public string Name { get; set; }

        public ICollection<PlaylistSong> PlaylistSongs { get; set; }
    }
}
