using Shuffull.Shared.Enums;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Shuffull.Shared.Networking.Models.Requests
{
    [Serializable]
    public class UpdateSongLastPlayedRequest : Request
    {
        public long SongId { get; set; }
        public DateTime LastPlayed { get; set; }

        public UpdateSongLastPlayedRequest()
        { 
            RequestType = RequestType.UpdateSongLastPlayed;
            RequestName = RequestType.UpdateSongLastPlayed.ToString();
        }
    }
}
