using Microsoft.EntityFrameworkCore.Metadata.Internal;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shuffull.Site.Database.Models
{
    public class Playlist
    {
        public const float PERCENT_UNTIL_REPLAYABLE_CAP = 0.9f;

        [Key]
        public long PlaylistId { get; set; }
        [Required]
        public long UserId { get; set; }
        [Required, NotNull]
        public string Name { get; set; }
        [Required]
        public long CurrentSongId { get; set; }
        [Required]
        [Range(0, PERCENT_UNTIL_REPLAYABLE_CAP)]
        [Column(TypeName = "decimal(2,2)")] // allows 2 digits to be saved, with up to 2 digits to the right of the decimal
        public decimal PercentUntilReplayable { get; set; }
        [Required]
        public long VersionCounter { get; set; }

        [Key]
        public User User { get; set; }
        public ICollection<PlaylistSong> PlaylistSongs { get; set; }
    }
}
