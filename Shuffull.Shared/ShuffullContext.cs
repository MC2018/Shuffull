using Microsoft.EntityFrameworkCore;
using MoreLinq.Extensions;
using Shuffull.Shared.Models;
using Shuffull.Shared.Models.Requests;
using Shuffull.Shared.Models.Server;
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
                    var directory = Path.GetDirectoryName(context._path);

                    if (!Directory.Exists(directory) && directory != string.Empty)
                    {
                        Directory.CreateDirectory(directory);
                    }

                    context.Database.Migrate();
                }
                catch (Exception e) // when the database changes too much, the schema needs to be rebuilt
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
                .HasValue<UpdateSongLastPlayedRequest>(Enums.RequestType.UpdateSongLastPlayed.ToString())
                .HasValue<AuthenticateRequest>(Enums.RequestType.Authenticate.ToString())
                .HasValue<CreateUserSongRequest>(Enums.RequestType.CreateUserSong.ToString())
                .HasValue<OverallSyncRequest>(Enums.RequestType.OverallSync.ToString());

            modelBuilder.Entity<UserSong>()
                .HasKey(us => new { us.UserId, us.SongId });

            modelBuilder.Entity<RecentlyPlayedSong>()
                .HasIndex(x => x.TimestampSeconds);
        }

        public async Task<RecentlyPlayedSong> GetCurrentlyPlayingSong()
        {
            return await RecentlyPlayedSongs
                .Where(x => x.TimestampSeconds != null)
                .Include(x => x.Song)
                .FirstOrDefaultAsync();
        }

        public async Task ClearCurrentlyPlayingSong()
        {
            var lastPlayedSongs = await RecentlyPlayedSongs.Where(x => x.TimestampSeconds != null).ToListAsync();

            if (!lastPlayedSongs.Any())
            {
                return;
            }

            foreach (var lastPlayedSong in lastPlayedSongs)
            {
                lastPlayedSong.TimestampSeconds = null;
            }
        }

        public async Task SetCurrentlyPlayingSong(long songId, string recentlyPlayedSongGuid = null)
        {
            await ClearCurrentlyPlayingSong();
            var song = await Songs.Where(x => x.SongId == songId).FirstOrDefaultAsync();

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
                    recentlyPlayedSong = await RecentlyPlayedSongs
                        .Where(x => x.RecentlyPlayedSongGuid == recentlyPlayedSongGuid)
                        .FirstOrDefaultAsync();

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

        public async Task UpdateCurrentlyPlayingSong(int timestampSeconds)
        {
            var recentlyPlayedSong = await RecentlyPlayedSongs.Where(x => x.TimestampSeconds != null).FirstOrDefaultAsync();

            if (recentlyPlayedSong == null)
            {
                throw new Exception("No song available");
            }

            recentlyPlayedSong.TimestampSeconds = timestampSeconds;
        }

        public async Task<RecentlyPlayedSong> CheckForLastRecentlyPlayedSong()
        {
            return await CheckForRecentlyPlayedSong(false);
        }

        public async Task<RecentlyPlayedSong> CheckForNextRecentlyPlayedSong()
        {
            return await CheckForRecentlyPlayedSong(true);
        }

        private async Task<RecentlyPlayedSong> CheckForRecentlyPlayedSong(bool next)
        {
            var lastPlayed = await RecentlyPlayedSongs
                .Where(x => x.TimestampSeconds != null)
                .Select(x => x.LastPlayed)
                .FirstOrDefaultAsync();

            if (lastPlayed == default)
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

            return await query.FirstOrDefaultAsync();
        }

        // User Song
        public async Task UpdateUserSongs(List<UserSong> userSongs)
        {
            var localSessionData = LocalSessionData.First();
            var songIds = userSongs.Select(x => x.SongId).ToList();
            var localUserSongs = await UserSongs
                .Where(x => x.UserId == localSessionData.UserId && songIds.Contains(x.SongId))
                .ToListAsync();

            if (localUserSongs != null)
            {
                UserSongs.RemoveRange(localUserSongs);
                UserSongs.AddRange(userSongs);
            }
            else
            {
                UserSongs.AddRange(userSongs);
            }
        }

        // Playlist
        public async Task UpdatePlaylist(Playlist playlist)
        {
            var localPlaylist = await Playlists
                .Where(x => x.PlaylistId == playlist.PlaylistId)
                .Include(x => x.PlaylistSongs)
                .FirstOrDefaultAsync();

            if (localPlaylist != null)
            {
                PlaylistSongs.RemoveRange(localPlaylist.PlaylistSongs);
                Playlists.Remove(localPlaylist);
                Playlists.Add(playlist);
            }
            else
            {
                Playlists.Add(playlist);
            }
        }

        public async void UpdateTags(List<Tag> tags)
        {
            var localTags = await Tags.ToListAsync();

            foreach (var tag in tags)
            {
                var tagToRemove = localTags.Where(x => x.TagId == tag.TagId).FirstOrDefault();

                if (tagToRemove != null)
                {
                    Tags.Remove(tagToRemove);
                }
            }

            await Tags.AddRangeAsync(tags);
        }

        public async Task<List<Playlist>> GetPlaylists()
        {
            return await Playlists
                .OrderBy(x => x.PlaylistId)
                .ToListAsync();
        }

        public async Task<Playlist> GetPlaylistWithSongs(long playlistId)
        {
            return await Playlists
                .Where(x => x.PlaylistId == playlistId)
                .Include(x => x.PlaylistSongs)
                .ThenInclude(x => x.Song)
                .OrderBy(x => x.PlaylistId)
                .FirstOrDefaultAsync();
        }

        public async Task<List<Request>> GetRequests()
        {
            return await Requests
                .OrderBy(x => x.TimeRequested)
                .ToListAsync();
        }

        public void ClearRecentlyPlayedSongs()
        {
            RecentlyPlayedSongs.RemoveRange(RecentlyPlayedSongs.ToList());
        }

        public async Task<Song> GetNextSong(long playlistId)
        {
            var localSessionData = await LocalSessionData.FirstAsync();
            var playlist = await Playlists.Where(x => x.PlaylistId == playlistId).FirstOrDefaultAsync();
            /*var userSongs = PlaylistSongs.Where(x => x.PlaylistId == playlistId)
                .SelectMany(x => x.Song.UserSongs)
                .Where(x => x.UserId == localSessionData.UserId)
                .OrderBy(x => x.LastPlayed)
                .ToList();*/
            var songs = await PlaylistSongs.Where(x => x.PlaylistId == playlistId)
                .Select(x => new
                {
                    x.Song,
                    LastPlayed = x.Song.UserSongs
                        .Where(y => y.UserId == localSessionData.UserId)
                        .Select(y => y.LastPlayed)
                        .FirstOrDefault()
                })
                .OrderBy(x => x.LastPlayed)
                .ToListAsync();

            if (!songs.Any())
            {
                throw new Exception("No song available");
            }

            var thresholdIndex = (int)Math.Ceiling(songs.Count * (1 - playlist.PercentUntilReplayable));
            var lastNeverPlayedSong = songs
                .Select((x, index) => new { SongInfo = x, Index = index })
                .Where(x => x.SongInfo.LastPlayed <= DateTime.MinValue)
                .Select(x => x.Index)
                .LastOrDefault();

            thresholdIndex = lastNeverPlayedSong > thresholdIndex ? lastNeverPlayedSong : thresholdIndex;

            var randomIndex = new Random().Next(0, thresholdIndex);
            var selectedUserSong = songs
                .Skip(randomIndex)
                .Take(1)
                .First();
            var result = await Songs.Where(x => x.SongId == selectedUserSong.Song.SongId).FirstAsync();
            return result;
        }
    }
}
