using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Features.Songs.UpdateSong;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Tests.Infrastructure;
using Shuffull.Shared.Enums;

namespace Shuffull.Core.Tests.Features.Songs.UpdateSong;

public class UpdateSongHandlerTest : IDisposable
{
    private readonly DatabaseFixture _database;
    private readonly UpdateSongHandler _handler;

    public UpdateSongHandlerTest()
    {
        _database = new DatabaseFixture();
        _handler = new UpdateSongHandler(_database.UnitOfWork);
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<Artist> SeedArtistAsync(string name)
    {
        var artist = new Artist { ArtistId = Guid.NewGuid().ToString(), Name = name };
        await _database.Context.Artists.AddAsync(artist);
        await _database.Context.SaveChangesAsync();
        return artist;
    }

    private async Task<Tag> SeedTagAsync(Tag tag)
    {
        tag.TagId = Guid.NewGuid().ToString();
        await _database.Context.Tags.AddAsync(tag);
        await _database.Context.SaveChangesAsync();
        return tag;
    }

    private async Task<Song> SeedSongAsync(DateTime version, IEnumerable<Artist>? artists = null, IEnumerable<Tag>? tags = null)
    {
        var song = new Song
        {
            SongId = Guid.NewGuid().ToString(),
            Name = "Original Name",
            FileExtension = ".mp3",
            FileHash = Guid.NewGuid().ToString(),
            Bpm = 120,
            Energy = 5,
            Version = version,
        };
        await _database.Context.Songs.AddAsync(song);
        await _database.Context.SaveChangesAsync();

        foreach (var artist in artists ?? Enumerable.Empty<Artist>())
        {
            await _database.Context.SongArtists.AddAsync(new SongArtist
            {
                SongArtistId = Guid.NewGuid().ToString(),
                SongId = song.SongId,
                ArtistId = artist.ArtistId,
            });
        }
        foreach (var tag in tags ?? Enumerable.Empty<Tag>())
        {
            await _database.Context.SongTags.AddAsync(new SongTag
            {
                SongTagId = Guid.NewGuid().ToString(),
                SongId = song.SongId,
                TagId = tag.TagId,
            });
        }
        await _database.Context.SaveChangesAsync();
        return song;
    }

    private async Task<List<string>> ArtistNamesForAsync(string songId) =>
        await _database.Context.SongArtists.AsNoTracking()
            .Where(sa => sa.SongId == songId)
            .Join(_database.Context.Artists, sa => sa.ArtistId, a => a.ArtistId, (_, a) => a.Name)
            .OrderBy(n => n)
            .ToListAsync();

    private async Task<List<(string Name, TagType Type)>> TagsForAsync(string songId) =>
        (await _database.Context.SongTags.AsNoTracking()
            .Where(st => st.SongId == songId)
            .Join(_database.Context.Tags, st => st.TagId, t => t.TagId, (_, t) => new { t.Name, t.Type })
            .ToListAsync())
            .Select(x => (x.Name, x.Type))
            .OrderBy(x => x.Name)
            .ToList();

    [Fact]
    public async Task Handle_UpdatesScalarsAndBumpsVersion()
    {
        var version = DateTime.UtcNow.AddDays(-3);
        var song = await SeedSongAsync(version);

        var result = await _handler.Handle(
            new UpdateSongCommand(song.SongId, "Corrected Name", Bpm: 140, Energy: 8, Artists: [], Tags: []),
            CancellationToken.None);

        Assert.True(result.IsOk, result.IsError ? result.GetError().Message : null);

        var saved = await _database.Context.Songs.AsNoTracking().SingleAsync(s => s.SongId == song.SongId);
        Assert.Equal("Corrected Name", saved.Name);
        Assert.Equal(140, saved.Bpm);
        Assert.Equal(8, saved.Energy);
        Assert.True(saved.Version > version);
        Assert.Equal(saved.Version, result.Get().Version);
    }

    [Fact]
    public async Task Handle_RewritesArtists_ReusingExistingAndCreatingNew()
    {
        var oldArtist = await SeedArtistAsync("Old Artist");
        var sharedArtist = await SeedArtistAsync("Shared Artist");
        var song = await SeedSongAsync(DateTime.UtcNow.AddDays(-1), artists: [oldArtist]);

        var result = await _handler.Handle(
            new UpdateSongCommand(song.SongId, "Name", null, null,
                Artists: ["Shared Artist", "Brand New Artist"], Tags: []),
            CancellationToken.None);

        Assert.True(result.IsOk, result.IsError ? result.GetError().Message : null);

        // The song now links to the reused + new artist, not the old one.
        Assert.Equal(["Brand New Artist", "Shared Artist"], await ArtistNamesForAsync(song.SongId));

        // "Shared Artist" was reused (no duplicate master row); "Old Artist" still exists but is unlinked.
        Assert.Equal(1, await _database.Context.Artists.CountAsync(a => a.Name == "Shared Artist"));
        Assert.Equal(1, await _database.Context.Artists.CountAsync(a => a.Name == "Brand New Artist"));
        Assert.Equal(1, await _database.Context.Artists.CountAsync(a => a.Name == "Old Artist"));
        Assert.Equal(sharedArtist.ArtistId,
            await _database.Context.SongArtists.AsNoTracking()
                .Where(sa => sa.SongId == song.SongId)
                .Join(_database.Context.Artists.Where(a => a.Name == "Shared Artist"), sa => sa.ArtistId, a => a.ArtistId, (sa, _) => sa.ArtistId)
                .SingleAsync());
    }

    [Fact]
    public async Task Handle_RewritesTags_ReusingByNameAndTypeAndCreatingNewSubtype()
    {
        var existingGenre = await SeedTagAsync(new Genre { Name = "House" });
        var song = await SeedSongAsync(DateTime.UtcNow.AddDays(-1), tags: [existingGenre]);

        var result = await _handler.Handle(
            new UpdateSongCommand(song.SongId, "Name", null, null, Artists: [],
                Tags: [new SongTagEdit("House", TagType.Genre), new SongTagEdit("Nighttime", TagType.Theme)]),
            CancellationToken.None);

        Assert.True(result.IsOk, result.IsError ? result.GetError().Message : null);

        Assert.Equal(
            [("House", TagType.Genre), ("Nighttime", TagType.Theme)],
            await TagsForAsync(song.SongId));

        // The genre was reused (still one row); the new Theme was created with the correct TPH subtype.
        Assert.Equal(1, await _database.Context.Tags.CountAsync(t => t.Name == "House"));
        var theme = await _database.Context.Tags.AsNoTracking().SingleAsync(t => t.Name == "Nighttime");
        Assert.Equal(TagType.Theme, theme.Type);
        Assert.IsType<Theme>(await _database.Context.Tags.SingleAsync(t => t.Name == "Nighttime"));
    }

    [Fact]
    public async Task Handle_WithEmptyArtistsAndTags_ClearsThem()
    {
        var artist = await SeedArtistAsync("Some Artist");
        var genre = await SeedTagAsync(new Genre { Name = "Trance" });
        var song = await SeedSongAsync(DateTime.UtcNow.AddDays(-1), artists: [artist], tags: [genre]);

        var result = await _handler.Handle(
            new UpdateSongCommand(song.SongId, "Name", null, null, Artists: [], Tags: []),
            CancellationToken.None);

        Assert.True(result.IsOk, result.IsError ? result.GetError().Message : null);
        Assert.Empty(await ArtistNamesForAsync(song.SongId));
        Assert.Empty(await TagsForAsync(song.SongId));
    }

    [Fact]
    public async Task Handle_DeduplicatesRepeatedAndBlankArtistEntries()
    {
        var song = await SeedSongAsync(DateTime.UtcNow.AddDays(-1));

        var result = await _handler.Handle(
            new UpdateSongCommand(song.SongId, "Name", null, null,
                Artists: ["Dupe", "Dupe", "  ", "Other"], Tags: []),
            CancellationToken.None);

        Assert.True(result.IsOk, result.IsError ? result.GetError().Message : null);
        Assert.Equal(["Dupe", "Other"], await ArtistNamesForAsync(song.SongId));
    }

    [Fact]
    public async Task Handle_WhenSongMissing_ReturnsError()
    {
        var result = await _handler.Handle(
            new UpdateSongCommand(Guid.NewGuid().ToString(), "Name", null, null, Artists: [], Tags: []),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("not found", result.GetError().Message, StringComparison.OrdinalIgnoreCase);
    }
}
