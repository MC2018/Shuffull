using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;

namespace Shuffull.Shared.Networking.Models.Server
{
    [Serializable]
    public class User
    {
        [Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
        public long UserId { get; set; }
        [Required]
        public string Username { get; set; }
        [Required]
        public DateTime Version { get; set; }

        public ICollection<Playlist> Playlists { get; set; }
        public ICollection<UserSong> UserSongs { get; set; }
    }
}
