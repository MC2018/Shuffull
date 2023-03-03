using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shuffull.Shared.Models
{
    [Index(nameof(Username))]
    public class User
    {
        [Key]
        public long UserId { get; set; }
        [Required, NotNull]
        public string Username { get; set; }

        public ICollection<Playlist> Playlists { get; set; }
    }
}
