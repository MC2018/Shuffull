using System;
using System.Collections.Generic;
using System.Text;

namespace Shuffull.Shared.Networking.Models.Requests
{
    [Serializable]
    public class UpdateSongLastPlayedRequest : IRequest
    {
        public string Guid { get; set; }
        public DateTime TimeRequested { get; set; }
        public long SongId { get; set; }
        public DateTime LastPlayed { get; set; }
    }
}
