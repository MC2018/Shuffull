using Shuffull.Core.Services.Alerts;

namespace Shuffull.Core.Tests.Infrastructure;

/// <summary>An <see cref="IHealthSignals"/> that remembers what was recorded, for code that records signals.</summary>
public sealed class RecordingHealthSignals : IHealthSignals
{
    public List<string> Successes { get; } = [];
    public List<(string Key, string Error)> Failures { get; } = [];

    public Task SuccessAsync(string key, CancellationToken cancellationToken = default)
    {
        Successes.Add(key);
        return Task.CompletedTask;
    }

    public Task FailureAsync(string key, string error, CancellationToken cancellationToken = default)
    {
        Failures.Add((key, error));
        return Task.CompletedTask;
    }
}

/// <summary>A clock the test moves by hand.</summary>
public sealed class ManualTimeProvider(DateTime utcNow) : TimeProvider
{
    public DateTime UtcNow { get; set; } = utcNow;

    public override DateTimeOffset GetUtcNow() => new(UtcNow, TimeSpan.Zero);
}
