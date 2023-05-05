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
    }
}
