﻿using Microsoft.EntityFrameworkCore.Metadata.Internal;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shuffull.Site.Models.Database
{
    [Microsoft.EntityFrameworkCore.Index(nameof(PlaylistId)), Microsoft.EntityFrameworkCore.Index(nameof(UserId))]
    public class Playlist
    {
        public const float PERCENT_UNTIL_REPLAYABLE_CAP = 0.9f;

        [Key]
        public string PlaylistId { get; set; }
        [Required]
        public string UserId { get; set; }
        [Required, NotNull]
        public string Name { get; set; }
        public string? CurrentSongId { get; set; }
        [Required]
        [Range(0, PERCENT_UNTIL_REPLAYABLE_CAP)]
        [Column(TypeName = "decimal(2,2)")] // allows 2 digits to be saved, with up to 2 digits to the right of the decimal
        public decimal PercentUntilReplayable { get; set; }
        [Required]
        public DateTime Version { get; set; }

        [Key]
        public User User { get; set; }
        public ICollection<PlaylistSong> PlaylistSongs { get; set; }
    }
}
