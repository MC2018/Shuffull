using System.Net;
using System.Text;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Nut.Results;
using Shuffull.Api.Services;
using Shuffull.Api.Services.FileStorage;
using Shuffull.Api.Tools.Authorization;
using Shuffull.Core.Alerts;
using Shuffull.Core.Features.Songs.ApplySongTags;
using Shuffull.Core.Features.Songs.GetLibrarySongs;
using Shuffull.Core.Features.Songs.GetPendingTagSongs;
using Shuffull.Core.Features.Songs.HoldTagSongs;
using Shuffull.Core.Persistence;
using Shuffull.Core.Services.Alerts;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Api;

/// <summary>
/// Every producer endpoint behind <see cref="ProducerKeyAttribute"/>, called the way the funnel calls it: the key in
/// the <c>X-Import-Key</c> header, same method and path as its clients send. The funnel runs these in prod, so a
/// mismatch here stops ingestion, tagging, lyrics, replacements and like-parity at once (Shuffull#45).
/// </summary>
public class ProducerKeyTest : IDisposable
{
    private const string Key = "import-key";

    private readonly DatabaseFixture _database = new();
    private readonly RecordingHealthSignals _signals = new();
    private GetPendingTagSongsQuery? _pendingQuery;

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private Task<ApiTestHost> StartAsync(string? configuredKey = Key) => ApiTestHost.StartAsync(services =>
    {
        var settings = new Dictionary<string, string?> { ["Shuffull:Files:SongImportDirectory"] = "/imports" };
        if (configuredKey != null)
        {
            settings[ProducerKeyAttribute.ConfigKey] = configuredKey;
        }
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetPendingTagSongsQuery>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<Result<PendingTagSongsResponse>>, CancellationToken>((query, _) => _pendingQuery = (GetPendingTagSongsQuery)query)
            .ReturnsAsync(Result.Ok(new PendingTagSongsResponse([], 0)));
        mediator.Setup(m => m.Send(It.IsAny<GetLibrarySongsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(new LibrarySongsPage([], null)));
        mediator.Setup(m => m.Send(It.IsAny<HoldTagSongsCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(new HoldTagSongsResponse(1)));
        mediator.Setup(m => m.Send(It.IsAny<ApplySongTagsCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(new ApplySongTagsResponse("song-1", 1, "gpt-5.5")));
        services.AddSingleton(mediator.Object);

        services.AddScoped<ShuffullContext>(_ => _database.CreateContext());
        services.AddSingleton<IHealthSignals>(_signals);
        services.AddSingleton(new Mock<IFileStorageService>().Object);
        services.AddScoped<SongImportIntakeService>();
    });

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    /// <summary>
    /// Each endpoint with the request the funnel sends and the status its action answers once past the key. The
    /// lyrics and import cases are built to stop early in the action (no such song, bad details) so they need no
    /// stored data, but they still only get there through the filter.
    /// </summary>
    public static TheoryData<string, HttpMethod, string, Func<HttpContent?>, HttpStatusCode> Endpoints => new()
    {
        { "rating queue", HttpMethod.Get, "api/v1/youtube-ratings?status=0", () => null, HttpStatusCode.OK },
        { "rating ack", HttpMethod.Post, "api/v1/youtube-ratings/ack", () => Json("[]"), HttpStatusCode.OK },
        {
            "lyrics", HttpMethod.Put, "api/v1/song-lyrics",
            () => Json("""{"externalSongId":"no-such-video","plainLyrics":"la la","instrumental":false}"""),
            HttpStatusCode.NotFound
        },
        {
            "import", HttpMethod.Post, "api/v1/song-imports",
            () =>
            {
                var form = new MultipartFormDataContent();
                form.Add(new StringContent("not json"), "details");
                form.Add(new ByteArrayContent([1, 2, 3]), "audio", "video.mp3");
                return form;
            },
            HttpStatusCode.BadRequest
        },
        { "replacement queue", HttpMethod.Get, "api/v1/replacements?status=0", () => null, HttpStatusCode.OK },
        { "pending tags", HttpMethod.Get, "api/v1/songs/pending-tags?limit=25", () => null, HttpStatusCode.OK },
        { "library", HttpMethod.Get, "api/v1/songs/library?limit=50", () => null, HttpStatusCode.OK },
        {
            "tag holds", HttpMethod.Post, "api/v1/songs/pending-tags/holds",
            () => Json("""{"songIds":["song-1"]}"""),
            HttpStatusCode.OK
        },
        {
            "apply tags", HttpMethod.Post, "api/v1/songs/song-1/tags",
            () => Json("""{"tags":{"mainGenres":["House"],"subGenres":[],"languages":["Instrumental"],"timePeriod":"2010s"},"tagModel":"gpt-5.5"}"""),
            HttpStatusCode.OK
        },
    };

    private static HttpRequestMessage Request(HttpMethod method, string path, HttpContent? content, string? key)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        if (key != null)
        {
            request.Headers.Add(ProducerKeyAttribute.HeaderName, key);
        }
        return request;
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task TheFunnelsRequest_ReachesTheEndpoint(
        string name, HttpMethod method, string path, Func<HttpContent?> content, HttpStatusCode expected)
    {
        await using var host = await StartAsync();

        var response = await host.Client.SendAsync(Request(method, path, content(), Key));

        Assert.True(expected == response.StatusCode, $"{name}: expected {expected}, got {response.StatusCode}");
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task AWrongOrMissingKey_Is401(
        string name, HttpMethod method, string path, Func<HttpContent?> content, HttpStatusCode _)
    {
        await using var host = await StartAsync();

        foreach (var key in new[] { null, "", "wrong", Key.ToUpperInvariant(), Key[..^1], Key + "x" })
        {
            var response = await host.Client.SendAsync(Request(method, path, content(), key));
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{name} with key '{key}': got {response.StatusCode}");
        }
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task NoKeyConfiguredOnTheSite_RefusesEveryone(
        string name, HttpMethod method, string path, Func<HttpContent?> content, HttpStatusCode _)
    {
        await using var host = await StartAsync(configuredKey: null);

        foreach (var key in new[] { null, "", Key })
        {
            var response = await host.Client.SendAsync(Request(method, path, content(), key));
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{name} with key '{key}': got {response.StatusCode}");
        }
    }

    [Fact]
    public async Task TheDeclaredModels_BindFromTheQueryNamesTheFunnelSends()
    {
        // A renamed parameter would bind as null, and the site would quietly judge by its own config again.
        await using var host = await StartAsync();

        var response = await host.Client.SendAsync(Request(HttpMethod.Get,
            "api/v1/songs/pending-tags?limit=25&weakModel=muse%20spark%2F1.2&strongModel=gpt-5.6-terra", null, Key));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new GetPendingTagSongsQuery(25, "muse spark/1.2", "gpt-5.6-terra"), _pendingQuery);
    }

    [Fact]
    public async Task AKeyInTheQueryString_IsNotAccepted()
    {
        // The funnel only ever sends the header; the old checks bound the key from the header alone too.
        await using var host = await StartAsync();

        var response = await host.Client.GetAsync($"api/v1/songs/pending-tags?limit=25&X-Import-Key={Key}&importKey={Key}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ARefusedPoll_RecordsNoSignal()
    {
        // The poll signals prove the funnel is asking for work; a caller with the wrong key must not keep them green.
        await using var host = await StartAsync();

        await host.Client.SendAsync(Request(HttpMethod.Get, "api/v1/songs/pending-tags?limit=25", null, "wrong"));
        await host.Client.SendAsync(Request(HttpMethod.Get, "api/v1/youtube-ratings?status=0", null, "wrong"));
        Assert.Empty(_signals.Successes);

        await host.Client.SendAsync(Request(HttpMethod.Get, "api/v1/songs/pending-tags?limit=25", null, Key));
        await host.Client.SendAsync(Request(HttpMethod.Get, "api/v1/youtube-ratings?status=0", null, Key));
        Assert.Equal([ShuffullSignalRules.PendingTagsPoll, ShuffullSignalRules.YoutubeRatingsPoll], _signals.Successes);
    }
}
