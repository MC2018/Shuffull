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
    public class Tag
    {
        [Key]
        public long TagId { get; set; }
        [Required, NotNull]
        public string Name { get; set; }

        public ICollection<SongTag> SongTags { get; set; }
    }
}
