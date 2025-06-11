using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Shuffull.Shared.Models.Server
{
    [Serializable]
    public class SongArtist
    {
        [Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
        public string SongArtistId { get; set; }
        [Required]
        public string SongId { get; set; }
        [Required]
        public string ArtistId { get; set; }

        [Key]
        public Artist Artist { get; set; }
        [Key]
        public Song Song { get; set; }
    }
}
