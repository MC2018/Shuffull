using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace Shuffull.Core.Models.Database
{
    /// <summary>
    /// Links a song to one of its artists. The key is the pair (SongId, ArtistId), set in ShuffullContext, so a song
    /// can't list the same artist twice. Its leading SongId column also serves lookups by song, so only ArtistId
    /// needs its own index.
    /// </summary>
    [Index(nameof(ArtistId))]
    public class SongArtist
    {
        [Required]
        public string SongId { get; set; }
        [Required]
        public string ArtistId { get; set; }

        public Artist Artist { get; set; }
        public Song Song { get; set; }
    }
}
