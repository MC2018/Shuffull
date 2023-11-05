using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Shuffull.Shared.Networking.Models.Server
{
    [Serializable]
    public class SongTag
    {
        [Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
        public long SongTagId { get; set; }
        [Required]
        public long SongId { get; set; }
        [Required]
        public long TagId { get; set; }

        [Key]
        public Song Song { get; set; }
        [Key]
        public Tag Tag { get; set; }
    }
}
