
using System;

namespace Shuffull.Shared.Networking.Models.Results
{
    [Serializable]
    public class Song
    {
        public long SongId { get; set; }
        public string Directory { get; set; }
        public string Name { get; set; }
    }
}
