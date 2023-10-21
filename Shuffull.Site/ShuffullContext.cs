using Microsoft.EntityFrameworkCore;
using Shuffull.Site.Database.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shuffull.Site
{
    public class ShuffullContext : DbContext
    {
        public DbSet<Song> Songs { get; set; }
        public DbSet<Artist> Artists { get; set; }
        public DbSet<SongArtist> SongArtists { get; set; }
        public DbSet<Playlist> Playlists { get; set; }
        public DbSet<PlaylistSong> PlaylistSongs { get; set; }
        public DbSet<User> Users { get; set; }

        public ShuffullContext(DbContextOptions options) : base(options) { }

        public void UpdateQueue(long songId)
        {
            var playlistIdsToUpdate = Songs
                .Where(x => x.SongId == songId)
                .SelectMany(x => x.PlaylistSongs)
                .Select(x => x.PlaylistId)
                .ToList();
            var playlistsToUpdate = Playlists
                .Where(x => playlistIdsToUpdate.Contains(x.PlaylistId))
                .Include(x => x.PlaylistSongs)
                .ToList();

            foreach (var playlist in playlistsToUpdate)
            {
                var playlistSongs = playlist.PlaylistSongs;
                var playlistSongCount = playlistSongs.Count();
                var inQueueCount = playlistSongs
                    .Where(x => x.InQueue)
                    .Count();
                var inQueuePercentage = (decimal)inQueueCount / playlistSongCount;

                if ((1 - inQueuePercentage) > playlist.PercentUntilReplayable)
                {
                    var addToQueueCount = (int)Math.Ceiling(((1 - inQueuePercentage) - playlist.PercentUntilReplayable) * playlistSongCount);
                    var playlistSongsToAdd = playlistSongs
                        .Where(x => !x.InQueue)
                        .OrderBy(x => x.LastPlayed)
                        .Take(addToQueueCount)
                        .ToList();

                    foreach (var playlistSong in playlistSongsToAdd)
                    {
                        playlistSong.InQueue = true;
                        playlistSong.LastAddedToQueue = DateTime.UtcNow;
                    }
                }
            }
        }
    }
}
