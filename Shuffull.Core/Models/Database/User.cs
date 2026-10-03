using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shuffull.Core.Models.Database
{
    [Index(nameof(Username)), Index(nameof(Version))]
    public class User
    {
        [Key]
        public string UserId { get; set; }
        [Required, NotNull]
        public string Username { get; set; }
        [Required]
        public DateTime Version { get; set; }
        [Required]
        [JsonIgnore]
        public string ServerHash { get; set; }

        /// <summary>
        /// Grants the <see cref="Authentication.Role.Curator"/> role: may edit shared song metadata to fix
        /// mislabels. Toggled manually in the database; read live on every request (no token reissue), so
        /// granting/revoking takes effect on the user's next call. Defaults to false for all existing rows.
        /// </summary>
        [Required]
        public bool IsCurator { get; set; }

        public ICollection<Playlist> Playlists { get; set; }
        public ICollection<UserSong> UserSongs { get; set; }
    }
}
