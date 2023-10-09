using Shuffull.Shared.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Shuffull.Shared.Networking.Models.Requests
{
    [Serializable]
    public class GetPlaylistsRequest : Request
    {
        public GetPlaylistsRequest()
        {
            RequestType = RequestType.UpdatePlaylists;
            RequestName = RequestType.UpdatePlaylists.ToString();
        }

        public override void UpdateLocalDb(ShuffullContext context)
        {

        }
    }
}
