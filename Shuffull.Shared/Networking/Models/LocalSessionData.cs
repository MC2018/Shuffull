using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Shuffull.Shared.Networking.Models
{
    [Serializable]
    public class LocalSessionData
    {
        [Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
        public long UserId { get; set; }
        [Required]
        public long CurrentPlaylistId { get; set; }
        [Required]
        public bool ActivelyDownload { get; set; }
        [Required]
        public string Token { get; set; }
        [Required]
        public DateTime Expiration { get; set; }
    }
}
