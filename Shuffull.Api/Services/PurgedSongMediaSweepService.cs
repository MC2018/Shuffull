using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence;
using Shuffull.Core.Services;

namespace Shuffull.Api.Services;

/// <summary>
/// Deletes the media of purged songs once their <see cref="SongTombstone.MediaGracePeriod"/> has passed. A purge
/// only writes the tombstone; nothing deletes a file at purge time.
/// <para>
/// Media is keyed by hash, so by the time the sweep reaches a tombstone the file may belong to something else. It is
/// left in place when a live song has the same <see cref="SongTombstone.FileHash"/> (the audio was re-imported, and
/// the new song writes to the same path), or when a newer tombstone with that hash is still inside its own grace
/// window (that purge's recovery window has to hold too). The older tombstone is then marked swept with
/// <c>MediaDeleted = false</c>, and the newer one deletes the file when its turn comes.
/// </para>
/// <para>
/// This uses a derived query rather than a queue: the work is every tombstone past its grace window with
/// <c>MediaSweptAt</c> still null. A failed delete leaves the row unswept and counts the failure. Failed rows sort
/// behind fresh ones, so a file that can't be deleted never starves the batch. After
/// <see cref="MaxDeleteAttempts"/> failures the row is abandoned with an error log: the file stays on disk, which
/// wastes space but loses nothing.
/// </para>
/// </summary>
public class PurgedSongMediaSweepService(IServiceProvider services, ISongMediaStore mediaStore, ILogger<PurgedSongMediaSweepService> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private const int BatchSize = 100;
    internal const int MaxDeleteAttempts = 10;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Purged-song media sweep failed; retrying next pass.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    /// <summary>One pass. Returns how many tombstones it looked at.</summary>
    internal async Task<int> SweepAsync(DateTime now, CancellationToken cancellationToken, int batchSize = BatchSize)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();

        var cutoff = now - SongTombstone.MediaGracePeriod;
        var due = await context.SongTombstones
            .Where(t => t.MediaSweptAt == null && t.DeletedAt <= cutoff)
            .OrderBy(t => t.MediaSweepFailures)
            .ThenBy(t => t.DeletedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        foreach (var tombstone in due)
        {
            var heldBy = await FindOtherHolderAsync(context, tombstone, cutoff, cancellationToken);
            if (heldBy is not null)
            {
                logger.LogInformation(
                    "Keeping media {FileHash} of purged song {SongId}: {HeldBy}.", tombstone.FileHash, tombstone.SongId, heldBy);
                MarkSwept(tombstone, now, mediaDeleted: false);
                continue;
            }

            var deleteResult = await mediaStore.DeleteSongMediaAsync(tombstone.FileHash, tombstone.FileExtension, cancellationToken);
            if (deleteResult.IsOk)
            {
                MarkSwept(tombstone, now, mediaDeleted: true);
                continue;
            }

            tombstone.MediaSweepFailures++;
            if (tombstone.MediaSweepFailures < MaxDeleteAttempts)
            {
                logger.LogWarning(
                    "Could not delete media of purged song {SongId} (attempt {Attempt} of {MaxAttempts}); retrying next pass: {Error}",
                    tombstone.SongId, tombstone.MediaSweepFailures, MaxDeleteAttempts, deleteResult.GetError().Message);
                continue;
            }

            logger.LogError(
                "Giving up on the media of purged song {SongId} after {Attempts} failed deletes; {FileHash}{FileExtension} stays on disk: {Error}",
                tombstone.SongId, tombstone.MediaSweepFailures, tombstone.FileHash, tombstone.FileExtension, deleteResult.GetError().Message);
            MarkSwept(tombstone, now, mediaDeleted: false);
        }

        await context.SaveChangesAsync(cancellationToken);
        return due.Count;
    }

    /// <summary>Says what else still needs this tombstone's file, or null when nothing does.</summary>
    private static async Task<string?> FindOtherHolderAsync(ShuffullContext context, SongTombstone tombstone, DateTime cutoff, CancellationToken cancellationToken)
    {
        if (await context.Songs.AnyAsync(s => s.FileHash == tombstone.FileHash, cancellationToken))
        {
            return "a live song uses the same file";
        }

        var newerPurgeInGraceWindow = await context.SongTombstones.AnyAsync(
            o => o.FileHash == tombstone.FileHash && o.SongId != tombstone.SongId && o.DeletedAt > cutoff,
            cancellationToken);
        return newerPurgeInGraceWindow ? "a newer purge of the same file is still in its grace window" : null;
    }

    private static void MarkSwept(SongTombstone tombstone, DateTime now, bool mediaDeleted)
    {
        tombstone.MediaSweptAt = now;
        tombstone.MediaDeleted = mediaDeleted;
    }
}
