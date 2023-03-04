using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shuffull.Database.Models
{
    [Index(nameof(Name))]
    public class Artist
    {
        [Key]
        public long ArtistId { get; set; }
        [Required]
        public string Name { get; set; }

    }
}
