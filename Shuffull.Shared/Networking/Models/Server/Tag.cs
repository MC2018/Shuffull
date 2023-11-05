using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;

namespace Shuffull.Shared.Networking.Models.Server
{
    [Serializable]
    public class Tag
    {
        [Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
        public long TagId { get; set; }
        [Required]
        public string Name { get; set; }

        public ICollection<SongTag> SongTags { get; set; }
    }
}
