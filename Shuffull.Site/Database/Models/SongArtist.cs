using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shuffull.Site.Database.Models
{
    public class SongArtist
    {
        [Key]
        public long SongArtistId { get; set; }
        public long SongId { get; set; }
        public long ArtistId { get; set; }

        [Key]
        public Song Song { get; set; }
        [Key]
        public Artist Artist { get; set; }
    }
}
