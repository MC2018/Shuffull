
using System;
using System.Collections.Generic;

namespace Shuffull.Shared.Networking.Models.Results
{
    [Serializable]
    public class Playlist
    {
        public long PlaylistId { get; set; }
        public long UserId { get; set; }
        public string Name { get; set; }
        public long CurrentSongId { get; set; }
        public decimal PercentUntilReplayable { get; set; }
        public DateTime LastUpdated { get; set; }
        public ICollection<PlaylistSong> PlaylistSongs { get; set; }
    }
}
