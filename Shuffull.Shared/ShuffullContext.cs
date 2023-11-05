using Microsoft.EntityFrameworkCore;
using MoreLinq.Extensions;
using Shuffull.Shared.Networking.Models;
using Shuffull.Shared.Networking.Models.Requests;
using Shuffull.Shared.Networking.Models.Server;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Shuffull.Shared
{
    public class ShuffullContext : DbContext
    {
        public DbSet<Artist> Artists { get; set; }
        public DbSet<Playlist> Playlists { get; set; }
        public DbSet<PlaylistSong> PlaylistSongs { get; set; }
        public DbSet<Song> Songs { get; set; }
        public DbSet<SongArtist> SongArtists { get; set; }
        public DbSet<SongTag> SongTags { get; set; }
        public DbSet<Tag> Tags { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<UserSong> UserSongs { get; set; }

        public DbSet<Request> Requests { get; set; }
        public DbSet<LocalSessionData> LocalSessionData { get; set; }
        public DbSet<RecentlyPlayedSong> RecentlyPlayedSongs { get; set; }

        private readonly string _path = "temp.db3";

        private static bool _initialized = false;

        public static void Initialize(ShuffullContext context)
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;

            if (context.Database.GetPendingMigrations().Any())
            {
                try
                {
                    context.Database.Migrate();
                }
                catch (Exception) // when the database changes too much, the schema needs to be rebuilt
                {
                    File.Delete(context._path);
                    context.Database.Migrate();
                }
            }
        }

        public ShuffullContext() : base()
        {
            Initialize(this);
        }

        public ShuffullContext(string path) : base()
        {
            _path = path;
            Initialize(this);
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
                .HasValue<GetPlaylistsRequest>(Enums.RequestType.UpdatePlaylists.ToString())
                .HasValue<UpdateSongLastPlayedRequest>(Enums.RequestType.UpdateSongLastPlayed.ToString());

            modelBuilder.Entity<RecentlyPlayedSong>()
                .HasIndex(x => x.TimestampSeconds);
        }

        public RecentlyPlayedSong GetCurrentlyPlayingSong()
        {
            return RecentlyPlayedSongs
                .Where(x => x.TimestampSeconds != null)
                .Include(x => x.Song)
                .FirstOrDefault();
        }

        public void ClearCurrentlyPlayingSong()
        {
            var lastPlayedSongs = RecentlyPlayedSongs.Where(x => x.TimestampSeconds != null).ToList();

            if (!lastPlayedSongs.Any())
            {
                return;
            }

            foreach (var lastPlayedSong in lastPlayedSongs)
            {
                lastPlayedSong.TimestampSeconds = null;
            }
        }

        public void SetCurrentlyPlayingSong(long songId, string recentlyPlayedSongGuid = null)
        {
            ClearCurrentlyPlayingSong();
            var song = Songs.Where(x => x.SongId == songId).FirstOrDefault();

            if (song == null)
            {
                throw new Exception("No song available");
            }

            if (songId != -1)
            {
                RecentlyPlayedSong recentlyPlayedSong;
                var existingRecordFound = false;

                if (recentlyPlayedSongGuid != null)
                {
                    recentlyPlayedSong = RecentlyPlayedSongs
                        .Where(x => x.RecentlyPlayedSongGuid == recentlyPlayedSongGuid)
                        .FirstOrDefault();

                    if (recentlyPlayedSong != null)
                    {
                        recentlyPlayedSong.TimestampSeconds = 0;
                        existingRecordFound = true;
                    }
                }

                if (!existingRecordFound)
                {
                    recentlyPlayedSong = new RecentlyPlayedSong()
                    {
                        RecentlyPlayedSongGuid = Guid.NewGuid().ToString(),
                        SongId = songId,
                        TimestampSeconds = 0,
                        LastPlayed = DateTime.UtcNow
                    };
                    RecentlyPlayedSongs.Add(recentlyPlayedSong);
                }
            }
        }

        public void UpdateCurrentlyPlayingSong(int timestampSeconds)
        {
            var recentlyPlayedSong = RecentlyPlayedSongs.Where(x => x.TimestampSeconds != null).FirstOrDefault();

            if (recentlyPlayedSong == null)
            {
                throw new Exception("No song available");
            }

            recentlyPlayedSong.TimestampSeconds = timestampSeconds;
        }

        public RecentlyPlayedSong CheckForLastRecentlyPlayedSong()
        {
            return CheckForRecentlyPlayedSong(false);
        }

        public RecentlyPlayedSong CheckForNextRecentlyPlayedSong()
        {
            return CheckForRecentlyPlayedSong(true);
        }

        private RecentlyPlayedSong CheckForRecentlyPlayedSong(bool next)
        {
            var lastPlayed = RecentlyPlayedSongs
                .Where(x => x.TimestampSeconds != null)
                .Select(x => x.LastPlayed)
                .FirstOrDefault();

            if (lastPlayed == null)
            {
                return null;
            }

            var query = RecentlyPlayedSongs
                .Include(x => x.Song)
                .Where(x => next ? (x.LastPlayed > lastPlayed) : (x.LastPlayed < lastPlayed));

            if (next)
            {
                query = query.OrderBy(x => x.LastPlayed);
            }
            else
            {
                query = query.OrderByDescending(x => x.LastPlayed);
            }

            return query.FirstOrDefault();
        }

        // Playlist
        public void UpdatePlaylist(Playlist playlist)
        {
            var localPlaylist = Playlists
                .Where(x => x.PlaylistId == playlist.PlaylistId)
                .Include(x => x.PlaylistSongs)
                .ThenInclude(x => x.Song)
                .FirstOrDefault();

            if (localPlaylist != null)
            {
                foreach (var playlistSong in localPlaylist.PlaylistSongs)
                {
                    Songs.Remove(playlistSong.Song);
                    PlaylistSongs.Remove(playlistSong);
                }

                Playlists.Remove(localPlaylist);
                Playlists.Add(playlist);
            }
            else
            {
                //playlist.PlaylistSongs = new List<PlaylistSong>();
                Playlists.Add(playlist);
            }
        }

        public List<Playlist> GetPlaylists()
        {
            return Playlists
                .OrderBy(x => x.PlaylistId)
                .ToList();
        }

        public Playlist GetPlaylistWithSongs(long playlistId)
        {
            return Playlists
                .Where(x => x.PlaylistId == playlistId)
                .Include(x => x.PlaylistSongs)
                .ThenInclude(x => x.Song)
                .OrderBy(x => x.PlaylistId)
                .FirstOrDefault();
        }

        public List<Request> GetRequests()
        {
            return Requests
                .OrderBy(x => x.TimeRequested)
                .ToList();
        }

        public void ClearRecentlyPlayedSongs()
        {
            RecentlyPlayedSongs.RemoveRange(RecentlyPlayedSongs.ToList());
        }

        public Song GetNextSong(long playlistId)
        {
            var localSessionData = LocalSessionData.First();
            var playlist = Playlists.Where(x => x.PlaylistId == playlistId).FirstOrDefault();
            var userSongs = PlaylistSongs.Where(x => x.PlaylistId == playlistId)
                .SelectMany(x => x.Song.UserSongs)
                .Where(x => x.UserId == localSessionData.UserId)
                .OrderBy(x => x.LastPlayed)
                .ToList();

            if (!userSongs.Any())
            {
                throw new Exception("No song available");
            }

            var thresholdIndex = (int)Math.Ceiling(userSongs.Count * playlist.PercentUntilReplayable);
            var lastNeverPlayedSong = userSongs
                .Select((x, index) => new { UserSong = x, Index = index })
                .Where(x => x.UserSong.LastPlayed <= DateTime.MinValue)
                .Select(x => x.Index)
                .Last();

            thresholdIndex = lastNeverPlayedSong > thresholdIndex ? lastNeverPlayedSong : thresholdIndex;

            var randomIndex = new Random().Next(0, thresholdIndex);
            var selectedUserSong = userSongs
                .Skip(randomIndex)
                .Take(1)
                .First();
            var result = Songs.Where(x => x.SongId == selectedUserSong.SongId).First();
            return result;
        }
    }
}
