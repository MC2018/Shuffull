
using System.Collections.Generic;

namespace Shuffull.Shared.Networking.Models
{
    public class PlaylistResult
    {
        public long PlaylistId { get; set; }
        public long UserId { get; set; }
        public string Name { get; set; }
        public long CurrentSongId { get; set; }
        public decimal PercentUntilReplayable { get; set; }
        public ICollection<SongResult> Songs{ get; set; }
    }
}
