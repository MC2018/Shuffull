﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shuffull.Shared.Networking.Models.Server
{
    [Serializable]
    public class Artist
    {
        [Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
        public long ArtistId { get; set; }
        [Required]
        public string Name { get; set; }

        public ICollection<SongArtist> SongArtists { get; set; }
    }
}
