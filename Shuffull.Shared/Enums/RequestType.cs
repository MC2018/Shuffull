using System;
using System.Collections.Generic;
using System.Text;

namespace Shuffull.Shared.Enums
{
    public enum RequestType
    {
        UpdateSongLastPlayed = 0,
        Authenticate = 1,
        OverallSync = 2,
        CreateUserSong = 3
    }
}
