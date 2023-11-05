using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Shuffull.Shared.Networking.Models.Server
{
    [Serializable]
    public class SongArtist
    {
        [Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
        public long SongArtistId { get; set; }
        [Required]
        public long SongId { get; set; }
        [Required]
        public long ArtistId { get; set; }

        [Key]
        public Artist Artist { get; set; }
        [Key]
        public Song Song { get; set; }
    }
}
