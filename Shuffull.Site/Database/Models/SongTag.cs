using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shuffull.Site.Database.Models
{
    [Index(nameof(SongId)), Index(nameof(TagId))]
    public class SongTag
    {
        [Key]
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
