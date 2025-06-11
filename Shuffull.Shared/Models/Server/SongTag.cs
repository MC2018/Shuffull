using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Shuffull.Shared.Models.Server
{
    [Serializable]
    public class SongTag
    {
        [Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
        public string SongTagId { get; set; }
        [Required]
        public string SongId { get; set; }
        [Required]
        public string TagId { get; set; }

        [Key]
        public Song Song { get; set; }
        [Key]
        public Tag Tag { get; set; }
    }
}
