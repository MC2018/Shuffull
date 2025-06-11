using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Shuffull.Shared.Enums;
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
    public class Tag
    {
        [Key]
        public string TagId { get; set; }
        [Required, NotNull]
        public string Name { get; set; }
        [Required, NotNull]
        public TagType Type { get; set; }

        public ICollection<SongTag> SongTags { get; set; }
    }
}
