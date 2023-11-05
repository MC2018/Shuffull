using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Shuffull.Shared.Networking.Models.Server
{
    [Serializable]
    public class PlaylistResponse
    {
        [Required]
        public User User { get; set; }
        [Required]
        public string Token { get; set; }
    }
}
