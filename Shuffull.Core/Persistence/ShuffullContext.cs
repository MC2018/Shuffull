using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Dtos;
using Shuffull.Shared.Enums;

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
    public DbSet<GenreJam> GenreJams { get; set; }
    public DbSet<SongDuplicateReport> SongDuplicateReports { get; set; }
    public DbSet<SongDuplicateReportAlias> SongDuplicateReportAliases { get; set; }
    public DbSet<SongDuplicateVerdict> SongDuplicateVerdicts { get; set; }
    public DbSet<SongMerge> SongMerges { get; set; }

    public ShuffullContext(DbContextOptions options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<UserSong>()
            .HasKey(us => new { us.UserId, us.SongId });

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

        modelBuilder.Entity<Tag>()
            .HasDiscriminator<TagType>("Type")
            .HasValue<Genre>(TagType.Genre)
            .HasValue<Language>(TagType.Language)
            .HasValue<TimePeriod>(TagType.TimePeriod)
            .HasValue<Mood>(TagType.Mood)
            .HasValue<Theme>(TagType.Theme);

        // A jam's filters are one JSON text column per side. Compared by their JSON, so editing a list in place is
        // seen as a change too.
        var filtersConverter = new ValueConverter<GenreJamFilters, string>(
            filters => GenreJamFilters.ToJson(filters),
            json => GenreJamFilters.FromJson(json));
        var filtersComparer = new ValueComparer<GenreJamFilters>(
            (a, b) => a == null ? b == null : b != null && GenreJamFilters.ToJson(a) == GenreJamFilters.ToJson(b),
            filters => GenreJamFilters.ToJson(filters).GetHashCode(),
            filters => GenreJamFilters.FromJson(GenreJamFilters.ToJson(filters)));
        modelBuilder.Entity<GenreJam>().Property(j => j.Whitelists).HasConversion(filtersConverter, filtersComparer);
        modelBuilder.Entity<GenreJam>().Property(j => j.Blacklists).HasConversion(filtersConverter, filtersComparer);
    }
}
