using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shuffull.Site.Models.Database
{
    [Index(nameof(SongId)), Index(nameof(ArtistId))]
    public class SongArtist
    {
        [Key]
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
