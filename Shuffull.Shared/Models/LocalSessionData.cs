using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Shuffull.Shared.Models
{
    [Serializable]
    public class LocalSessionData
    {
        [Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
        public string UserId { get; set; }
        [Required]
        public string CurrentPlaylistId { get; set; }
        [Required]
        public bool ActivelyDownload { get; set; }
        [Required]
        public string Token { get; set; }
        [Required]
        public DateTime Expiration { get; set; }
    }
}
