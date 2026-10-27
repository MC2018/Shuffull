using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence;
using Shuffull.Core.Services.Alerts;

namespace Shuffull.Core.Alerts;

/// <summary>
/// Writes each signal from its own short-lived <see cref="ShuffullContext"/>, so a caller's half-finished unit of work
/// is never saved by accident, and a caller whose context just failed can still record that.
///
/// <para>Repeated successes are throttled to one write per key per <see cref="SuccessWriteInterval"/>. The funnel's
/// polls arrive every 10 minutes, but a caller retrying in a tight loop (a misbehaving client, a hand-run script)
/// shouldn't turn into a write per request. A success after a failure is always written, since it is the one that
/// clears the failure. Failures are always written; they are rarer, and each one carries its error.</para>
/// </summary>
public sealed class HealthSignals(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<HealthSignals> logger)
    : IHealthSignals
{
    public static readonly TimeSpan SuccessWriteInterval = TimeSpan.FromMinutes(1);

    // Per key, in this process: when a success was last written, or null when the last write was a failure.
    private readonly ConcurrentDictionary<string, DateTime?> _lastSuccessWrite = new();

    public Task SuccessAsync(string key, CancellationToken cancellationToken = default)
    {
        key = HealthSignal.NormalizeKey(key);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (_lastSuccessWrite.TryGetValue(key, out var last) && last is { } at && now - at < SuccessWriteInterval)
        {
            return Task.CompletedTask;
        }

        _lastSuccessWrite[key] = now;
        return WriteAsync(key, signal => signal.RecordSuccess(now), cancellationToken);
    }

    public Task FailureAsync(string key, string error, CancellationToken cancellationToken = default)
    {
        key = HealthSignal.NormalizeKey(key);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        _lastSuccessWrite[key] = null;
        return WriteAsync(key, signal => signal.RecordFailure(error, now), cancellationToken);
    }

    private async Task WriteAsync(string key, Action<HealthSignal> record, CancellationToken cancellationToken)
    {
        // Two requests can create the same key at once; the loser's insert hits the primary key and retries as an update.
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
                var signal = await db.HealthSignals.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
                if (signal == null)
                {
                    signal = HealthSignal.Create(key);
                    db.HealthSignals.Add(signal);
                }

                record(signal);
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (DbUpdateException) when (attempt == 1)
            {
            }
            catch (Exception e)
            {
                // Forget the throttle so the next success tries again instead of waiting out the interval.
                _lastSuccessWrite.TryRemove(key, out _);
                logger.LogWarning(e, "Could not record the health signal {Key}.", key);
                return;
            }
        }
    }
}
