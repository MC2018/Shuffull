using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Shuffull.Shared.Models.Server
{
    [Serializable]
    public class PlaylistSong
    {
        [Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
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
