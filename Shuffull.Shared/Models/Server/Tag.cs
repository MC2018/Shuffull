using Shuffull.Shared.Enums;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;

namespace Shuffull.Shared.Models.Server
{
    [Serializable]
    public class Tag
    {
        [Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
        public long TagId { get; set; }
        [Required]
        public string Name { get; set; }
        [Required]
        public TagType Type { get; set; }

        public ICollection<SongTag> SongTags { get; set; }
    }
}
