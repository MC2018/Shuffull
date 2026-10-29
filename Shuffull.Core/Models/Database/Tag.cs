using Microsoft.EntityFrameworkCore;
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

namespace Shuffull.Core.Models.Database
{
    // Name is unique across every tag type: the tagging paths look tags up by name alone (Shuffull#43).
    [Index(nameof(Type)), Index(nameof(Name), IsUnique = true)]
    public abstract class Tag
    {
        [Key]
        public string TagId { get; set; }
        [Required, NotNull]
        public string Name { get; set; }
        [Required]
        public TagType Type { get; protected set; }

        public ICollection<SongTag> SongTags { get; set; }
    }
}
