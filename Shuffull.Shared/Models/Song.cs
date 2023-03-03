using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shuffull.Shared.Models
{
    [Index(nameof(Name))]
    public class Song
    {
        [Key]
        public long SongId { get; set; }
        [Required]
        public string Directory { get; set; } = string.Empty;
        [Required]
        public string Name { get; set; } = string.Empty;

        public ICollection<PlaylistSong> PlaylistSongs { get; set; }
    }
}
