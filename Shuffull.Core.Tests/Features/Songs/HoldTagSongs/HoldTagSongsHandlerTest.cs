using Nut.Results;
using Shuffull.Core.Features.Songs.GetPendingTagSongs;
using Shuffull.Core.Features.Songs.HoldTagSongs;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Tests.Infrastructure;
using Shuffull.Shared.Tools;

namespace Shuffull.Core.Tests.Features.Songs.HoldTagSongs;

/// <summary>
/// The producer's hold report on its own. How it plays with the queue's ladder over several cycles is in
/// GetPendingTagSongsHandlerTest; this covers what one report may and may not change.
/// </summary>
public class HoldTagSongsHandlerTest : IDisposable
{
    private readonly DatabaseFixture _database = new();
    private readonly ManualTimeProvider _clock = new(new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc));

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<Song> SeedAsync(int tagAttempts = 0, DateTime? nextTagAttemptAt = null)
    {
        var song = new Song
        {
            SongId = IdGenerator.Generate(),
            Name = $"song-{IdGenerator.Generate()[..6]}",
            FileExtension = ".mp3",
            FileHash = IdGenerator.Generate(),
            TagAttempts = tagAttempts,
            NextTagAttemptAt = nextTagAttemptAt,
            Version = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        _database.Context.Songs.Add(song);
        await _database.Context.SaveChangesAsync();
        return song;
    }

    private async Task<HoldTagSongsResponse> HoldAsync(params string[] songIds)
    {
        await using var context = _database.CreateContext();
        return (await new HoldTagSongsHandler(context, _clock).Handle(new HoldTagSongsCommand(songIds), CancellationToken.None)).Get();
    }

    private async Task<Song> ReloadAsync(string songId)
    {
        await using var context = _database.CreateContext();
        return (await context.Songs.FindAsync(songId))!;
    }

    [Fact]
    public async Task AHeldSong_StartsItsLadderAgain()
    {
        var song = await SeedAsync(tagAttempts: 6, nextTagAttemptAt: _clock.UtcNow.AddHours(1));

        Assert.Equal(new HoldTagSongsResponse(1), await HoldAsync(song.SongId));

        var reloaded = await ReloadAsync(song.SongId);
        Assert.Equal(0, reloaded.TagAttempts);
        Assert.Equal(_clock.UtcNow + GetPendingTagSongsHandler.FirstRetryDelay, reloaded.NextTagAttemptAt);
        Assert.Equal(song.Version, reloaded.Version); // queue state, not a change clients sync
    }

    [Fact]
    public async Task AHoldReport_NeverPushesADueTimeBack()
    {
        // A late report: now + 5 minutes would be later than the due time the hand-out set.
        var dueSoon = _clock.UtcNow.AddMinutes(2);
        var song = await SeedAsync(tagAttempts: 1, nextTagAttemptAt: dueSoon);

        await HoldAsync(song.SongId);

        var reloaded = await ReloadAsync(song.SongId);
        Assert.Equal(dueSoon, reloaded.NextTagAttemptAt);
        Assert.Equal(0, reloaded.TagAttempts);
    }

    [Fact]
    public async Task ASongTaggedSinceTheHandOut_OrUnknown_IsLeftAlone()
    {
        // ApplySongTags clears NextTagAttemptAt, so a report that arrives after the write must not put it back.
        var taggedSince = await SeedAsync();

        Assert.Equal(new HoldTagSongsResponse(0), await HoldAsync(taggedSince.SongId, "no-such-song", " "));

        var reloaded = await ReloadAsync(taggedSince.SongId);
        Assert.Null(reloaded.NextTagAttemptAt);
        Assert.Equal(0, reloaded.TagAttempts);
    }

    [Fact]
    public async Task AnEmptyReport_IsANoOp()
        => Assert.Equal(new HoldTagSongsResponse(0), await HoldAsync());

    [Fact]
    public void AReport_IsCappedAtOnePage()
    {
        var validator = new HoldTagSongsValidator();
        var page = Enumerable.Range(0, GetPendingTagSongsHandler.MaxLimit).Select(i => $"song-{i}").ToList();

        Assert.True(validator.Validate(new HoldTagSongsCommand(page)).IsValid);
        Assert.False(validator.Validate(new HoldTagSongsCommand([.. page, "one-more"])).IsValid);
        Assert.False(validator.Validate(new HoldTagSongsCommand(null!)).IsValid);
    }
}
