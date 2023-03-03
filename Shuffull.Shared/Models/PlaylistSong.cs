using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shuffull.Shared.Models
{
    public class PlaylistSong
    {
        [Key]
        public long PlaylistSongId { get; set; }
        [Required]
        public long PlaylistId { get; set; }
        [Required]
        public long SongId { get; set; }
        [Required]
        public long BucketNumber { get; set; }
        [Required]
        public DateTime LastAddedToQueue { get; set; }

        public bool IsInQueue() { return BucketNumber != -1; }

        [Key]
        public Playlist Playlist { get; set; }
        [Key]
        public Song Song { get; set; }
    }
}
