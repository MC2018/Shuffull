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
    [Index(nameof(PlaylistId)), Index(nameof(SongId))]
    public class PlaylistSong
    {
        [Key]
        public string PlaylistSongId { get; set; }
        [Required]
        public string PlaylistId { get; set; }
        [Required]
        public string SongId { get; set; }

        [Key]
        public Playlist Playlist { get; set; }
        [Key]
        public Song Song { get; set; }
    }
}
