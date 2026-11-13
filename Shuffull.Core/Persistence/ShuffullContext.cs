using Microsoft.EntityFrameworkCore;
using Shuffull.Core.Features.SongRequests;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;

namespace Shuffull.Core.Persistence;

/// <summary>
/// The server-side EF Core context for Shuffull. It lives in Shuffull.Core (provider-agnostic) so it
/// can be shared by any host. The concrete database provider is configured by the host at
/// registration time (e.g. UseNpgsql), and the EF migrations live in the Shuffull.Api host.
/// </summary>
public class ShuffullContext : DbContext
{
    public DbSet<Artist> Artists { get; set; }
    public DbSet<Playlist> Playlists { get; set; }
    public DbSet<PlaylistSong> PlaylistSongs { get; set; }
    public DbSet<Song> Songs { get; set; }
    public DbSet<SongArtist> SongArtists { get; set; }
    public DbSet<SongTag> SongTags { get; set; }
    public DbSet<SongImport> SongImports { get; set; }
    public DbSet<SongReplacement> SongReplacements { get; set; }
    public DbSet<SongTombstone> SongTombstones { get; set; }
    public DbSet<YoutubeRatingRequest> YoutubeRatingRequests { get; set; }
    public DbSet<SongRequest> SongRequests { get; set; }
    public DbSet<Tag> Tags { get; set; }
    public DbSet<GenreRelation> GenreRelations { get; set; }
    public DbSet<Genre> Genres { get; set; }
    public DbSet<Language> Languages { get; set; }
    public DbSet<TimePeriod> TimePeriods { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<UserSong> UserSongs { get; set; }
    public DbSet<HealthSignal> HealthSignals { get; set; }
    public DbSet<AlertState> AlertStates { get; set; }
    public DbSet<Listen> Listens { get; set; }
    public DbSet<UserDevice> UserDevices { get; set; }
    public DbSet<PlaybackSession> PlaybackSessions { get; set; }

    public ShuffullContext(DbContextOptions options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<UserSong>()
            .HasKey(us => new { us.UserId, us.SongId });

        modelBuilder.Entity<SongArtist>()
            .HasKey(sa => new { sa.SongId, sa.ArtistId });

        // Per user, so the same install signed into two accounts is two devices (Shuffull#132).
        modelBuilder.Entity<UserDevice>()
            .HasKey(d => new { d.UserId, d.DeviceId });

        // One live row per device, sharing the device's key (Shuffull#6). Deleting the device deletes its session; the
        // session prune never deletes the device.
        modelBuilder.Entity<PlaybackSession>(session =>
        {
            session.HasKey(s => new { s.UserId, s.DeviceId });
            session.HasOne(s => s.Device)
                .WithOne()
                .HasForeignKey<PlaybackSession>(s => new { s.UserId, s.DeviceId })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GenreRelation>()
            .HasOne(gr => gr.MainGenre)
            .WithMany(gr => gr.GenreRelationsAsMain)
            .HasForeignKey(gr => gr.MainGenreId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<GenreRelation>()
            .HasOne(gr => gr.SubGenre)
            .WithMany(gr => gr.GenreRelationsAsSub)
            .HasForeignKey(gr => gr.SubGenreId)
            .OnDelete(DeleteBehavior.Restrict);

        // One open request per user and song (Shuffull#57). Filtered, so finished and cancelled rows stay as history
        // and the song can be asked for again. Plain SQL that Postgres and SQLite (the tests) both accept.
        modelBuilder.Entity<SongRequest>()
            .HasIndex(r => new { r.UserId, r.ExternalSongId })
            .IsUnique()
            .HasFilter($"\"Status\" IN ({string.Join(", ", SongRequestRules.Open.Select(s => (int)s))})")
            .HasDatabaseName("IX_SongRequests_UserId_ExternalSongId_Open");

        // One weekly requests playlist per user and week, so the instant path and an import can't both create it
        // (see SongRequestRules.PlaylistNamePrefix). Filtered to those names only: other playlists may share a name.
        modelBuilder.Entity<Playlist>()
            .HasIndex(p => new { p.UserId, p.Name })
            .IsUnique()
            .HasFilter($"\"Name\" LIKE '{SongRequestRules.PlaylistNamePrefix}%'")
            .HasDatabaseName("IX_Playlists_UserId_Name_SongRequests");

        modelBuilder.Entity<Tag>()
            .HasDiscriminator<TagType>("Type")
            .HasValue<Genre>(TagType.Genre)
            .HasValue<Language>(TagType.Language)
            .HasValue<TimePeriod>(TagType.TimePeriod)
            .HasValue<Mood>(TagType.Mood)
            .HasValue<Theme>(TagType.Theme);
    }
}
