using System;
using System.Collections.Generic;
using System.Text;

namespace Shuffull.Shared.Networking.Models.Requests
{
    [Serializable]
    public class UpdateSongsLastPlayedRequest : IRequest
    {
        public string Guid { get; set; }
        public DateTime TimeRequested { get; set; }
        public List<UpdateSongLastPlayedRequest> Songs { get; set; }
    }
}
