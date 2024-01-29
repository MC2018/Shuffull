using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Shuffull.Shared.Models.Server
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
        public ICollection<UserSong> UserSongs { get; set; }
        public ICollection<SongArtist> SongArtists { get; set; }
        public ICollection<SongTag> SongTags { get; set; }
    }
}
