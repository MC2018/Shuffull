using Shuffull.Shared.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Shuffull.Shared.Networking.Models.Requests
{
    [Serializable]
    public class UpdatePlaylistsRequest : Request
    {
        public UpdatePlaylistsRequest()
        {
            RequestType = RequestType.UpdatePlaylists;
            RequestName = RequestType.UpdatePlaylists.ToString();
        }
    }
}
