using Microsoft.EntityFrameworkCore;
using Shuffull.Shared.Networking.Models;
using Shuffull.Shared.Networking.Models.Requests;
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
        public DbSet<Playlist> Playlists { get; set; }
        public DbSet<PlaylistSong> PlaylistSongs { get; set; }
        public DbSet<Request> Requests { get; set; }
        public DbSet<RecentlyPlayedSong> RecentlyPlayedSongs { get; set; }

        private readonly string _path = "temp.db3";

        public ShuffullContext() : base()
        {
        }

        public ShuffullContext(string path) : base()
        {
            _path = path;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            base.OnConfiguring(optionsBuilder);
            optionsBuilder.UseSqlite($"Filename={_path}");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Request>()
                .ToTable("Requests")
                .HasDiscriminator<string>("RequestName")
                .HasValue<UpdatePlaylistsRequest>(Enums.RequestType.UpdatePlaylists.ToString())
                .HasValue<UpdateSongLastPlayedRequest>(Enums.RequestType.UpdateSongLastPlayed.ToString());

            modelBuilder.Entity<RecentlyPlayedSong>()
                .HasIndex(x => x.TimestampSeconds);
        }
    }
}
