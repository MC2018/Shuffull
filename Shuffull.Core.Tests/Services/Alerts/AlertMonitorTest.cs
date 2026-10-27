using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Nut.Results;
using Shuffull.Api.Services.Alerts;
using Shuffull.Core.Services.Alerts;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Services.Alerts;

/// <summary>
/// One monitor run end to end over a real database: an alert emails once, isn't re-sent by later runs, survives a
/// failed send (retried next run), and is left alone when its check can't run.
/// </summary>
public class AlertMonitorTest : IDisposable
{
    private static readonly DateTime Start = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);

    private readonly DatabaseFixture _database = new();
    private readonly FakeCheck _check = new();
    private readonly FakeMailer _mailer = new();
    private readonly RecordingHealthSignals _signals = new();
    private readonly ManualTimeProvider _clock = new(Start);
    private readonly AlertMonitor _monitor;

    public AlertMonitorTest()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _database.CreateContext());
        services.AddScoped<IAlertCheck>(_ => _check);
        _monitor = new AlertMonitor(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), _mailer,
            _signals, new AlertsConfiguration { SubjectPrefix = "[Shuffull]" }, _clock, NullLogger<AlertMonitor>.Instance);
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class FakeCheck : IAlertCheck
    {
        public List<AlertFinding> Findings { get; } = [];
        public bool Fail { get; set; }
        public string KeyPrefix => "fake";

        public Task<Result<IReadOnlyList<AlertFinding>>> EvaluateAsync(DateTime nowUtc, CancellationToken cancellationToken)
            => Task.FromResult(Fail
                ? Result.Error<IReadOnlyList<AlertFinding>>("database unavailable")
                : Result.Ok<IReadOnlyList<AlertFinding>>(Findings.ToList()));
    }

    private sealed class FakeMailer : IAlertMailer
    {
        public List<string> Sent { get; } = [];
        public bool Fail { get; set; }

        public Task<Result> SendAsync(string subject, string textBody, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                return Task.FromResult(Result.Error("Resend is down"));
            }

            Sent.Add(subject);
            return Task.FromResult(Result.Ok());
        }
    }

    private Task RunAsync(TimeSpan? after = null)
    {
        _clock.UtcNow += after ?? TimeSpan.Zero;
        return _monitor.RunOnceAsync(CancellationToken.None);
    }

    [Fact]
    public async Task NewAlert_EmailsOnce_AndLaterRunsStayQuiet()
    {
        _check.Findings.Add(new AlertFinding("fake:broken", 2, "Something broke", "details"));

        await RunAsync();
        await RunAsync(TimeSpan.FromMinutes(5));

        Assert.Equal(["[Shuffull] Something broke"], _mailer.Sent);
        var state = await _database.CreateContext().AlertStates.SingleAsync();
        Assert.Equal(2, state.Level);
        Assert.Equal(Start, state.FiringSinceUtc);
    }

    [Fact]
    public async Task ClearedAlert_ResolvesOnlyAfterStayingClear()
    {
        _check.Findings.Add(new AlertFinding("fake:broken", 2, "Something broke", "details"));
        await RunAsync();

        _check.Findings.Clear();
        await RunAsync(TimeSpan.FromMinutes(5));
        await RunAsync(TimeSpan.FromMinutes(5));
        Assert.Single(_mailer.Sent);

        await RunAsync(TimeSpan.FromMinutes(15));
        Assert.Equal("[Shuffull] Resolved: Something broke", _mailer.Sent[^1]);
        Assert.Equal(0, (await _database.CreateContext().AlertStates.SingleAsync()).Level);
    }

    [Fact]
    public async Task FailedSend_IsRetriedNextRun()
    {
        _check.Findings.Add(new AlertFinding("fake:broken", 2, "Something broke", "details"));
        _mailer.Fail = true;
        await RunAsync();
        Assert.Empty(_mailer.Sent);

        _mailer.Fail = false;
        await RunAsync(TimeSpan.FromMinutes(5));
        Assert.Equal(["[Shuffull] Something broke"], _mailer.Sent);
    }

    [Fact]
    public async Task CheckThatCantRun_LeavesItsAlertsOpen_AndRecordsWhy()
    {
        _check.Findings.Add(new AlertFinding("fake:broken", 2, "Something broke", "details"));
        await RunAsync();

        _check.Fail = true;
        await RunAsync(TimeSpan.FromHours(2));

        Assert.Single(_mailer.Sent); // no resolve
        Assert.Equal(2, (await _database.CreateContext().AlertStates.SingleAsync()).Level);
        var (key, error) = Assert.Single(_signals.Failures);
        Assert.Equal("monitor:fake", key);
        Assert.Contains("database unavailable", error);
    }

    [Fact]
    public async Task EscalationEmailsAgain()
    {
        _check.Findings.Add(new AlertFinding("fake:broken", 1, "Getting worse", "details"));
        await RunAsync();

        _check.Findings[0] = new AlertFinding("fake:broken", 2, "Now broken", "details");
        await RunAsync(TimeSpan.FromMinutes(5));

        Assert.Equal(["[Shuffull] Getting worse", "[Shuffull] Now broken"], _mailer.Sent);
    }

    [Fact]
    public async Task KeyOutsideTheChecksPrefix_IsIgnored()
    {
        _check.Findings.Add(new AlertFinding("other:broken", 2, "Not mine", "details"));

        await RunAsync();

        Assert.Empty(_mailer.Sent);
        Assert.Empty(await _database.CreateContext().AlertStates.ToListAsync());
    }
}
