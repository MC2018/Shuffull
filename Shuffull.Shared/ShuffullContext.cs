using Microsoft.EntityFrameworkCore;
using Shuffull.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shuffull.Shared
{
    public class ShuffullContext : DbContext
    {
        public DbSet<Song> Songs { get; set; }
        public DbSet<Artist> Artists { get; set; }
        public DbSet<SongArtist> SongArtists { get; set; }

        public ShuffullContext(DbContextOptions options) : base(options) { }
    }
}
