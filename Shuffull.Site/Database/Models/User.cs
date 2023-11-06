﻿using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shuffull.Site.Database.Models
{
    [Index(nameof(Username)), Index(nameof(Version))]
    public class User
    {
        [Key]
        public long UserId { get; set; }
        [Required, NotNull]
        public string Username { get; set; }
        [Required]
        public DateTime Version { get; set; }
        [Required]
        [JsonIgnore]
        public string ServerHash { get; set; }

        public ICollection<Playlist> Playlists { get; set; }
        public ICollection<UserSong> UserSongs { get; set; }
    }
}
