using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Features.Songs.ApplySongTags;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Tests.Infrastructure;
using Shuffull.Metadata.Models;
using Shuffull.Metadata.Tools;
using Shuffull.Shared.Tools;

namespace Shuffull.Core.Tests.Features.Songs;

/// <summary>
/// The producer write-back. The load-bearing assertions are the things it must NOT do: never lock the song
/// (which would exclude it from all future upgrades), never touch curator-owned name/artists, and never accept
/// tags without provenance (which would leave the song re-queueing forever).
/// </summary>
public class ApplySongTagsHandlerTest : IDisposable
{
    private readonly DatabaseFixture _database;

    public ApplySongTagsHandlerTest() => _database = new DatabaseFixture();

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private static ModelStrengths Strengths() => new(new Dictionary<string, int>
    {
        ["weak-model"] = 10,
        ["strong-model"] = 30,
    });

    private static GeneratedSongTags Tags(params string[] genres)
        => new([.. genres], [], ["English"], "2010s", ["Energetic"], 7, null);

    private async Task<Song> SeedAsync(string? tagModel = null, bool locked = false, string name = "Original Name")
    {
        var song = new Song
        {
            SongId = IdGenerator.Generate(),
            Name = name,
            FileExtension = ".mp3",
            FileHash = IdGenerator.Generate(),
            TagModel = tagModel,
            MetadataLocked = locked,
            Version = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        _database.Context.Songs.Add(song);
        await _database.Context.SaveChangesAsync();
        return song;
    }

    private Task<Result<ApplySongTagsResponse>> RunAsync(string songId, GeneratedSongTags tags, string model)
        => new ApplySongTagsHandler(_database.Context, Strengths())
            .Handle(new ApplySongTagsCommand(songId, tags, model), CancellationToken.None);

    private async Task<Song> ReloadAsync(string songId)
    {
        _database.Context.ChangeTracker.Clear();
        return await _database.Context.Songs.Include(s => s.SongTags).AsNoTracking().SingleAsync(s => s.SongId == songId);
    }

    [Fact]
    public async Task WritesTags_StampsTagModel_AndBumpsVersion()
    {
        var song = await SeedAsync();
        var before = song.Version;

        var result = await RunAsync(song.SongId, Tags("Rock", "Pop"), "weak-model");

        Assert.True(result.IsOk);
        var reloaded = await ReloadAsync(song.SongId);
        Assert.Equal("weak-model", reloaded.TagModel);
        Assert.True(reloaded.Version > before);
        Assert.NotEmpty(reloaded.SongTags);
    }

    [Fact]
    public async Task NeverLocksTheSong()
    {
        // Locking here would quietly exclude the song from every future upgrade — the exact trap that makes
        // UpdateSong the wrong door for a producer.
        var song = await SeedAsync();

        await RunAsync(song.SongId, Tags("Rock"), "weak-model");

        Assert.False((await ReloadAsync(song.SongId)).MetadataLocked);
    }

    [Fact]
    public async Task LeavesCuratorOwnedNameAlone()
    {
        var song = await SeedAsync(name: "Curator Cleaned Title");

        await RunAsync(song.SongId, Tags("Rock"), "weak-model");

        Assert.Equal("Curator Cleaned Title", (await ReloadAsync(song.SongId)).Name);
    }

    [Fact]
    public async Task ReplacesPreviousTagsRatherThanAccumulating()
    {
        var song = await SeedAsync();
        await RunAsync(song.SongId, Tags("Rock"), "weak-model");
        var firstCount = (await ReloadAsync(song.SongId)).SongTags.Count;

        await RunAsync(song.SongId, Tags("Jazz"), "strong-model");
        var reloaded = await ReloadAsync(song.SongId);

        Assert.Equal(firstCount, reloaded.SongTags.Count); // same shape, not doubled
        Assert.Equal("strong-model", reloaded.TagModel);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RejectsTagsWithoutProvenance(string model)
    {
        var song = await SeedAsync();

        var result = await RunAsync(song.SongId, Tags("Rock"), model);

        Assert.True(result.IsError);
        Assert.Empty((await ReloadAsync(song.SongId)).SongTags);
    }

    [Fact]
    public async Task RejectsLockedSong_WhenNotAnUpgrade()
    {
        var song = await SeedAsync(tagModel: "strong-model", locked: true);

        var result = await RunAsync(song.SongId, Tags("Rock"), "weak-model");

        Assert.True(result.IsError);
        Assert.Contains("curator-locked", result.GetError().Message);
        Assert.Equal("strong-model", (await ReloadAsync(song.SongId)).TagModel);
    }

    [Fact]
    public async Task RejectsLockedSong_WithNoPriorModel()
    {
        // Hand-tagged by a person: nothing to upgrade FROM, so the weak model must not overwrite it.
        var song = await SeedAsync(tagModel: null, locked: true);

        Assert.True((await RunAsync(song.SongId, Tags("Rock"), "weak-model")).IsError);
    }

    [Fact]
    public async Task AllowsLockedSong_WhenItIsAGenuineUpgrade()
    {
        var song = await SeedAsync(tagModel: "weak-model", locked: true);

        var result = await RunAsync(song.SongId, Tags("Rock"), "strong-model");

        Assert.True(result.IsOk);
        var reloaded = await ReloadAsync(song.SongId);
        Assert.Equal("strong-model", reloaded.TagModel);
        Assert.True(reloaded.MetadataLocked); // the lock itself survives
    }

    [Fact]
    public async Task UnknownSongIsAnError()
    {
        Assert.True((await RunAsync("does-not-exist", Tags("Rock"), "weak-model")).IsError);
    }
}
