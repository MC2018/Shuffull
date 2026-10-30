using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Nut.Results;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Tests.Infrastructure;
using static Shuffull.Core.Tests.Infrastructure.SongImportHarness;
using Shuffull.Metadata.Contracts;
using Shuffull.Metadata.Enums;
using Shuffull.Shared.Tools;

namespace Shuffull.Core.Tests.Services;

/// <summary>
/// The hook start point (the app's audition start, as a percent of the song) travels from the producer's hand-off,
/// through the SongImport row, onto the Song and out in the sync payload (Shuffull#79). A payload from a producer
/// that doesn't send it has to read as unknown, not as 0% (the very start of the song), and a re-source that doesn't
/// know it must not erase the one the song already has.
/// </summary>
public class SongImportHookStartTest : IDisposable
{
    private static readonly DateTime SongVersion = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly DatabaseFixture _database = new();

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private static SongImportDetails Details(double? hookStartPercent) => new(
        ExternalSongId: "ext-1", FileExtension: ".mp3", ExternalPlaylistId: "pl-1", TargetUserId: "user-1",
        TargetPlaylistId: null, TargetPlaylistName: null, ExternalSource: ExternalSource.Youtube,
        HookStartPercent: hookStartPercent);

    [Fact]
    public void HookStartPercent_SurvivesTheRoundTrip()
    {
        var json = JsonConvert.SerializeObject(Details(62.5));

        var read = JsonConvert.DeserializeObject<SongImportDetails>(json);

        Assert.NotNull(read);
        Assert.Equal(62.5, read.HookStartPercent);
    }

    [Fact]
    public void HookStartPercent_LeftUnset_RoundTripsAsNull()
    {
        var json = JsonConvert.SerializeObject(Details(null));

        var read = JsonConvert.DeserializeObject<SongImportDetails>(json);

        Assert.NotNull(read);
        Assert.Null(read.HookStartPercent);
    }

    [Fact]
    public void OlderPayload_WithoutTheField_ReadsAsUnknown()
    {
        const string json = """
            {"ExternalSongId":"ext-1","FileExtension":".mp3","ExternalPlaylistId":"pl-1","TargetUserId":"user-1",
             "TargetPlaylistId":null,"TargetPlaylistName":null,"ExternalSource":1,"TagRevision":2}
            """;

        var read = JsonConvert.DeserializeObject<SongImportDetails>(json);

        Assert.NotNull(read);
        Assert.Equal("ext-1", read.ExternalSongId);
        Assert.Null(read.HookStartPercent);
    }

    // --- Intake: the hand-off lands on the SongImport row ---

    private async Task<double?> IntakeAsync(double? hookStartPercent)
    {
        var details = Details(hookStartPercent) with { Name = "Song Name", Artists = ["Artist"] };
        var result = await SongImportHarness.Intake(_database).CreateImportAsync(details, [1, 2, 3]);
        Assert.True(result.IsOk, result.IsError ? result.GetError().Message : null);
        var row = await _database.CreateContext().SongImports.AsNoTracking().SingleAsync(i => i.SongImportId == result.Get());
        return row.HookStartPercent;
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(37.25)]
    [InlineData(100.0)]
    public async Task Intake_CarriesTheProducersHookStart(double percent)
    {
        Assert.Equal(percent, await IntakeAsync(percent));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-0.5)]
    [InlineData(100.5)]
    public async Task Intake_StoresAnUnusableHookStartAsUnknown(double percent)
    {
        // A NaN on a Song would fail every sync that includes it: System.Text.Json refuses to write one.
        Assert.Null(await IntakeAsync(percent));
    }

    [Fact]
    public async Task Intake_WithoutAHookStart_StoresUnknown()
    {
        Assert.Null(await IntakeAsync(null));
    }

    // --- Import: the SongImport row lands on the Song ---

    private async Task SeedUserAsync()
    {
        _database.Context.Users.Add(new User { UserId = "user-1", Username = "user", Version = SongVersion, ServerHash = "server-hash" });
        await _database.Context.SaveChangesAsync();
    }

    private async Task SeedSongAsync(double? hookStartPercent, bool metadataLocked = false)
    {
        _database.Context.Songs.Add(new Song
        {
            SongId = "song-1",
            Name = "Everybody Wants You",
            FileExtension = ".mp3",
            FileHash = Hasher.ShaHash(Mp3Frames()).Substring(0, 32),
            ExternalSongId = "yt-1",
            HookStartPercent = hookStartPercent,
            MetadataLocked = metadataLocked,
            Version = SongVersion,
        });
        await _database.Context.SaveChangesAsync();
    }

    private static SongImport NewImport(double? hookStartPercent, string? replacesSongId = null) => new()
    {
        SongImportId = Guid.NewGuid().ToString(),
        Name = "Everybody Wants You",
        ImportFolder = "folder",
        FileType = ".mp3",
        UserId = "user-1",
        ExternalSongId = "yt-1",
        HookStartPercent = hookStartPercent,
        ReplacesSongId = replacesSongId,
        LastUpdatedAt = DateTime.UtcNow,
    };

    /// <summary>Runs <paramref name="songImport"/> through the real import loop and returns the song it became.</summary>
    private async Task<Song> RunImportAsync(SongImport songImport, byte[] audio)
    {
        var import = await SongImportHarness.RunImportAsync(_database, songImport, audio);
        Assert.Equal(SongImportState.Completed, import.State);
        return await _database.CreateContext().Songs.AsNoTracking().SingleAsync(s => s.SongId == import.SongId);
    }

    [Fact]
    public async Task Import_NewSong_StoresTheHookStart()
    {
        await SeedUserAsync();

        var song = await RunImportAsync(NewImport(62.5), Mp3Frames());

        Assert.Equal(62.5, song.HookStartPercent);
    }

    [Fact]
    public async Task Import_NewSongWithoutAHookStart_StoresUnknown()
    {
        await SeedUserAsync();

        var song = await RunImportAsync(NewImport(null), Mp3Frames());

        Assert.Null(song.HookStartPercent);
    }

    [Fact]
    public async Task Reimport_WithAHookStart_OverwritesTheOldOne()
    {
        // Same video, new audio: the producer re-converted it, so the import refreshes the song in place.
        await SeedUserAsync();
        await SeedSongAsync(hookStartPercent: 20);

        var song = await RunImportAsync(NewImport(45), Mp3Frames(fill: 7));

        Assert.Equal("song-1", song.SongId);
        Assert.Equal(45, song.HookStartPercent);
        Assert.True(song.Version > SongVersion);
    }

    [Fact]
    public async Task Reimport_WithoutAHookStart_KeepsTheOldOne()
    {
        await SeedUserAsync();
        await SeedSongAsync(hookStartPercent: 20);

        var song = await RunImportAsync(NewImport(null), Mp3Frames(fill: 7));

        Assert.Equal("song-1", song.SongId);
        Assert.Equal(20, song.HookStartPercent);
    }

    [Fact]
    public async Task Replacement_OfACuratorLockedSong_StillTakesTheNewHookStart()
    {
        // The lock protects hand-edited metadata. The hook start is measured off the audio, which the replacement
        // swaps, and nobody edits it by hand.
        await SeedUserAsync();
        await SeedSongAsync(hookStartPercent: 20, metadataLocked: true);

        var song = await RunImportAsync(NewImport(45, replacesSongId: "song-1"), Mp3Frames(fill: 7));

        Assert.Equal(45, song.HookStartPercent);
        Assert.Equal("Everybody Wants You", song.Name);
    }

    // --- Sync payload ---

    // What the controllers write with: AddControllers() with no JSON options set.
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static SongDto Dto(double? hookStartPercent) => SongDto.Create(new Song
    {
        SongId = "song-1", Name = "Name", FileExtension = ".mp3", FileHash = "hash", Version = SongVersion,
        HookStartPercent = hookStartPercent,
    }).Get();

    [Fact]
    public void SyncPayload_CarriesTheHookStart()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(Dto(62.5), Web);

        Assert.Contains("\"hookStartPercent\":62.5", json);
    }

    [Fact]
    public void SyncPayload_WithoutAHookStart_SendsNull()
    {
        using var json = JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(Dto(null), Web));

        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("hookStartPercent").ValueKind);
    }
}
