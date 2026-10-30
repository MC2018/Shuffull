using System.Net;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nut.Results;
using Shuffull.Api.Configuration;
using Shuffull.Api.Controllers;
using Shuffull.Api.Services.Alerts;
using Shuffull.Core.Alerts;
using Shuffull.Core.Features.Songs.GetPendingTagSongs;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Services.Alerts;

/// <summary>
/// The places that write the signals the alert rules read. If one of these stops recording, or records the wrong key,
/// its alert goes silent with no error, which is the failure this whole feature exists to catch.
/// </summary>
public class AlertRecordingTest : IDisposable
{
    private readonly DatabaseFixture _database = new();
    private readonly RecordingHealthSignals _signals = new();

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    // ── FunnelProbe ──────────────────────────────────────────────────────────────────────────────────────

    private sealed class StubHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(respond());
        }
    }

    private Task ProbeAsync(StubHandler handler, string url = "http://youtubefunnel.web-api:8080/api/health")
        => new FunnelProbe(new HttpClient(handler), new AlertsConfiguration { FunnelHealthUrl = url }, _signals,
                NullLogger<FunnelProbe>.Instance)
            .RunAsync(DateTime.UtcNow, CancellationToken.None);

    [Fact]
    public async Task FunnelProbe_Success_RecordsUp()
    {
        await ProbeAsync(new StubHandler(() => new HttpResponseMessage(HttpStatusCode.OK)));

        Assert.Equal([ShuffullSignalRules.FunnelProbe], _signals.Successes);
        Assert.Empty(_signals.Failures);
    }

    [Fact]
    public async Task FunnelProbe_ServerError_RecordsDown()
    {
        await ProbeAsync(new StubHandler(() => new HttpResponseMessage(HttpStatusCode.BadGateway)));

        var (key, error) = Assert.Single(_signals.Failures);
        Assert.Equal(ShuffullSignalRules.FunnelProbe, key);
        Assert.Contains("502", error);
    }

    [Fact]
    public async Task FunnelProbe_ConnectionFailure_RecordsDown()
    {
        await ProbeAsync(new StubHandler(() => throw new HttpRequestException("Name or service not known")));

        var (key, error) = Assert.Single(_signals.Failures);
        Assert.Equal(ShuffullSignalRules.FunnelProbe, key);
        Assert.Contains("Name or service not known", error);
    }

    [Fact]
    public async Task FunnelProbe_BlankUrl_ProbesNothing()
    {
        var handler = new StubHandler(() => new HttpResponseMessage(HttpStatusCode.OK));
        await ProbeAsync(handler, url: "");

        Assert.Equal(0, handler.Calls);
        Assert.Empty(_signals.Successes);
        Assert.Empty(_signals.Failures);
    }

    // ── The funnel's two polls ───────────────────────────────────────────────────────────────────────────

    private SongsController Songs()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetPendingTagSongsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(new PendingTagSongsResponse([], 0)));
        return new SongsController(mediator.Object, _signals);
    }

    [Fact]
    public async Task PendingTagsPoll_FromTheFunnel_IsRecorded()
    {
        await Songs().GetPendingTagSongs(25, CancellationToken.None);
        Assert.Equal([ShuffullSignalRules.PendingTagsPoll], _signals.Successes);
    }

    private YoutubeRatingsController Ratings() => new(_database.CreateContext(), _signals);

    [Fact]
    public async Task YoutubeRatingsPoll_ForPending_IsRecorded()
    {
        await Ratings().GetQueue(YoutubeRatingStatus.Pending, CancellationToken.None);
        Assert.Equal([ShuffullSignalRules.YoutubeRatingsPoll], _signals.Successes);
    }

    [Fact]
    public async Task YoutubeRatingsPoll_ForAnotherStatus_IsNotRecorded()
    {
        await Ratings().GetQueue(YoutubeRatingStatus.Completed, CancellationToken.None);
        Assert.Empty(_signals.Successes);
    }
}
