using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Alerts;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Services.Alerts;

/// <summary>The recorder writes from its own scope, throttles repeated successes, and never throttles a failure.</summary>
public class HealthSignalsTest : IDisposable
{
    private static readonly DateTime Start = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);

    private readonly DatabaseFixture _database = new();
    private readonly ManualTimeProvider _clock = new(Start);
    private readonly HealthSignals _signals;

    public HealthSignalsTest()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _database.CreateContext());
        _signals = new HealthSignals(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), _clock,
            NullLogger<HealthSignals>.Instance);
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private Task<HealthSignal> ReadAsync(string key) =>
        _database.CreateContext().HealthSignals.AsNoTracking().SingleAsync(s => s.Key == key);

    [Fact]
    public async Task RepeatedSuccesses_AreWrittenOncePerInterval()
    {
        await _signals.SuccessAsync("poll");
        _clock.UtcNow = Start.AddSeconds(30);
        await _signals.SuccessAsync("poll");
        Assert.Equal(Start, (await ReadAsync("poll")).LastSuccessUtc);

        _clock.UtcNow = Start + HealthSignals.SuccessWriteInterval;
        await _signals.SuccessAsync("poll");
        Assert.Equal(_clock.UtcNow, (await ReadAsync("poll")).LastSuccessUtc);
    }

    [Fact]
    public async Task Failures_CountUp_FromTheFirstOne()
    {
        await _signals.SuccessAsync("sweep");
        _clock.UtcNow = Start.AddSeconds(5);
        await _signals.FailureAsync("sweep", "disk unavailable");
        _clock.UtcNow = Start.AddSeconds(10);
        await _signals.FailureAsync("sweep", "still unavailable");

        var signal = await ReadAsync("sweep");
        Assert.Equal(2, signal.ConsecutiveFailures);
        Assert.Equal(Start.AddSeconds(5), signal.FailingSinceUtc);
        Assert.Equal("still unavailable", signal.LastError);
        Assert.Equal(Start, signal.LastSuccessUtc);
    }

    [Fact]
    public async Task SuccessAfterAFailure_IsWrittenAtOnce_AndClearsTheRun()
    {
        await _signals.SuccessAsync("sweep");
        _clock.UtcNow = Start.AddSeconds(5);
        await _signals.FailureAsync("sweep", "disk unavailable");
        _clock.UtcNow = Start.AddSeconds(10); // well inside the success throttle
        await _signals.SuccessAsync("sweep");

        var signal = await ReadAsync("sweep");
        Assert.Equal(0, signal.ConsecutiveFailures);
        Assert.Null(signal.FailingSinceUtc);
        Assert.Equal(Start.AddSeconds(10), signal.LastSuccessUtc);
    }

    [Fact]
    public async Task LongError_IsTruncated()
    {
        await _signals.FailureAsync("x", new string('e', HealthSignal.MaxErrorLength + 50));
        Assert.Equal(HealthSignal.MaxErrorLength, (await ReadAsync("x")).LastError!.Length);
    }
}
