using Microsoft.EntityFrameworkCore;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence;

namespace Shuffull.Api.Services;

/// <summary>
/// Deletes playback sessions nobody has written for <see cref="PlaybackSession.RetentionPeriod"/> (Shuffull#6), so an
/// old phone's last song doesn't sit in the "play on" list forever. Only the session goes; the device row stays. A
/// derived query, so a failed pass simply matches the same rows next time.
/// </summary>
public class PlaybackSessionPruneService(IServiceProvider services, TimeProvider timeProvider, ILogger<PlaybackSessionPruneService> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PruneAsync(timeProvider.GetUtcNow().UtcDateTime, stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning(e, "Playback session prune failed; retrying next pass.");
            }
            await Task.Delay(Interval, stoppingToken);
        }
    }

    /// <summary>One pass. Returns how many sessions it deleted.</summary>
    internal async Task<int> PruneAsync(DateTime now, CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
        var cutoff = now - PlaybackSession.RetentionPeriod;
        var deleted = await context.PlaybackSessions
            .Where(s => s.UpdatedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted > 0)
        {
            logger.LogInformation("Pruned {Count} playback sessions untouched since {Cutoff:o}.", deleted, cutoff);
        }
        return deleted;
    }
}
