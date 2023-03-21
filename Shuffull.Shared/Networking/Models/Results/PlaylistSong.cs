
using System;

namespace Shuffull.Shared.Networking.Models.Results
{
    [Serializable]
    public class PlaylistSong
    {
        public long PlaylistSongId { get; set; }
        public long PlaylistId { get; set; }
        public long SongId { get; set; }
        public DateTime LastAddedToQueue { get; set; }
        public DateTime LastPlayed { get; set; }
        public bool InQueue { get; set; }

        public Song Song { get; set; }
    }
}
