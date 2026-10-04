using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Nut.Results;
using Shuffull.Api.Services;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Services;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Services;

/// <summary>
/// The second half of a purge: a tombstone's media is deleted only after the grace window, never while a live
/// song shares its file hash, and a failed delete is retried on the next pass instead of being marked done.
/// </summary>
public class PurgedSongMediaSweepServiceTest : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);

    private readonly DatabaseFixture _database;
    private readonly RecordingMediaStore _mediaStore = new();
    private readonly PurgedSongMediaSweepService _service;

    public PurgedSongMediaSweepServiceTest()
    {
        _database = new DatabaseFixture();
        var services = new ServiceCollection();
        services.AddScoped(_ => _database.CreateContext());
        _service = new PurgedSongMediaSweepService(services.BuildServiceProvider(), _mediaStore, NullLogger<PurgedSongMediaSweepService>.Instance);
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class RecordingMediaStore : ISongMediaStore
    {
        public List<string> Deleted { get; } = [];
        public bool Fail { get; set; }

        public Task<Result> DeleteSongMediaAsync(string fileHash, string fileExtension, CancellationToken cancellationToken = default)
        {
            if (Fail)
            {
                return Task.FromResult(Result.Error(new Exception("disk unavailable")));
            }

            Deleted.Add(fileHash);
            return Task.FromResult(Result.Ok());
        }
    }

    private async Task<SongTombstone> SeedTombstoneAsync(TimeSpan age, string? fileHash = null)
    {
        var tombstone = new SongTombstone
        {
            SongId = Guid.NewGuid().ToString(),
            Name = "Song",
            FileHash = fileHash ?? Guid.NewGuid().ToString(),
            FileExtension = ".mp3",
            DeletedByUserId = "user",
            DeletedAt = Now - age,
        };
        await _database.Context.SongTombstones.AddAsync(tombstone);
        await _database.Context.SaveChangesAsync();
        return tombstone;
    }

    private async Task<SongTombstone> ReloadAsync(string songId) =>
        await _database.Context.SongTombstones.AsNoTracking().SingleAsync(t => t.SongId == songId);

    [Fact]
    public async Task Sweep_InsideGraceWindow_LeavesMediaAlone()
    {
        var tombstone = await SeedTombstoneAsync(SongTombstone.MediaGracePeriod - TimeSpan.FromMinutes(1));

        var handled = await _service.SweepAsync(Now, CancellationToken.None);

        Assert.Equal(0, handled);
        Assert.Empty(_mediaStore.Deleted);
        Assert.Null((await ReloadAsync(tombstone.SongId)).MediaSweptAt);
    }

    [Fact]
    public async Task Sweep_PastGraceWindow_DeletesMediaAndMarksTombstone()
    {
        var tombstone = await SeedTombstoneAsync(SongTombstone.MediaGracePeriod + TimeSpan.FromMinutes(1));

        var handled = await _service.SweepAsync(Now, CancellationToken.None);

        Assert.Equal(1, handled);
        Assert.Equal([tombstone.FileHash], _mediaStore.Deleted);
        var reloaded = await ReloadAsync(tombstone.SongId);
        Assert.Equal(Now, reloaded.MediaSweptAt);
        Assert.True(reloaded.MediaDeleted);

        // Swept rows are done: a second pass does nothing.
        Assert.Equal(0, await _service.SweepAsync(Now.AddDays(1), CancellationToken.None));
        Assert.Single(_mediaStore.Deleted);
    }

    [Fact]
    public async Task Sweep_FileHashSharedByLiveSong_KeepsMedia()
    {
        // e.g. the same audio re-imported during the grace window: same hash, same path, now a live song's file.
        var sharedHash = Guid.NewGuid().ToString();
        await _database.Context.Songs.AddAsync(new Song
        {
            SongId = Guid.NewGuid().ToString(),
            Name = "Live",
            FileExtension = ".mp3",
            FileHash = sharedHash,
            Version = Now,
        });
        await _database.Context.SaveChangesAsync();
        var tombstone = await SeedTombstoneAsync(SongTombstone.MediaGracePeriod + TimeSpan.FromDays(1), sharedHash);

        await _service.SweepAsync(Now, CancellationToken.None);

        Assert.Empty(_mediaStore.Deleted);
        var reloaded = await ReloadAsync(tombstone.SongId);
        Assert.Equal(Now, reloaded.MediaSweptAt);
        Assert.False(reloaded.MediaDeleted);
    }

    [Fact]
    public async Task Sweep_DeleteFails_LeavesTombstoneForNextPass()
    {
        var tombstone = await SeedTombstoneAsync(SongTombstone.MediaGracePeriod + TimeSpan.FromDays(1));
        _mediaStore.Fail = true;

        await _service.SweepAsync(Now, CancellationToken.None);

        Assert.Null((await ReloadAsync(tombstone.SongId)).MediaSweptAt);

        _mediaStore.Fail = false;
        await _service.SweepAsync(Now, CancellationToken.None);

        Assert.Equal([tombstone.FileHash], _mediaStore.Deleted);
        Assert.True((await ReloadAsync(tombstone.SongId)).MediaDeleted);
    }
}
