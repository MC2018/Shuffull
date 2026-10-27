namespace Shuffull.Core.Services.Alerts;

/// <summary>
/// Records the outcome of recurring work into <c>HealthSignal</c> rows for the alert monitor to read (Shuffull#75).
/// Callers fire and forget: a failure to record is logged and swallowed, because a broken health table must never
/// break the work it watches.
/// </summary>
public interface IHealthSignals
{
    Task SuccessAsync(string key, CancellationToken cancellationToken = default);

    Task FailureAsync(string key, string error, CancellationToken cancellationToken = default);
}
