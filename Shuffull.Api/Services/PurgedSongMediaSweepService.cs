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
/// Before deleting, the sweep checks that no live song has the same <see cref="SongTombstone.FileHash"/>. Media is
/// keyed by hash, so a song re-imported from the same audio during the grace window writes to the same path and
/// owns that file now. A tombstone that loses this check is marked swept with <c>MediaDeleted = false</c> and never
/// looked at again.
/// </para>
/// <para>
/// This uses a derived query rather than a queue: the work is every tombstone past its grace window with
/// <c>MediaSweptAt</c> still null. A failed pass leaves those rows null, so the next pass simply picks them up again.
/// </para>
/// </summary>
public class PurgedSongMediaSweepService(IServiceProvider services, ISongMediaStore mediaStore, ILogger<PurgedSongMediaSweepService> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private const int BatchSize = 100;

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

    /// <summary>One pass. Returns how many tombstones it handled.</summary>
    internal async Task<int> SweepAsync(DateTime now, CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();

        var cutoff = now - SongTombstone.MediaGracePeriod;
        var due = await context.SongTombstones
            .Where(t => t.MediaSweptAt == null && t.DeletedAt <= cutoff)
            .OrderBy(t => t.DeletedAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var tombstone in due)
        {
            var hashInUse = await context.Songs.AnyAsync(s => s.FileHash == tombstone.FileHash, cancellationToken);
            if (hashInUse)
            {
                logger.LogInformation(
                    "Keeping media {FileHash} of purged song {SongId}: a live song uses the same file.",
                    tombstone.FileHash, tombstone.SongId);
            }
            else
            {
                var deleteResult = await mediaStore.DeleteSongMediaAsync(tombstone.FileHash, tombstone.FileExtension, cancellationToken);
                if (deleteResult.IsError)
                {
                    // Left unswept, so the next pass tries again.
                    logger.LogWarning("Could not delete media of purged song {SongId}: {Error}", tombstone.SongId, deleteResult.GetError().Message);
                    continue;
                }
            }

            tombstone.MediaSweptAt = now;
            tombstone.MediaDeleted = !hashInUse;
        }

        await context.SaveChangesAsync(cancellationToken);
        return due.Count;
    }
}
