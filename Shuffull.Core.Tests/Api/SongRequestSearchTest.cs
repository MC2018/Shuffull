using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shuffull.Api.Services.SourceSearch;
using Shuffull.Core.Behaviors;
using Shuffull.Core.Features.Capabilities.GetCapabilities;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Tests.Infrastructure;
using Shuffull.Metadata.Contracts;

namespace Shuffull.Core.Tests.Api;

/// <summary>
/// The song search relay (Shuffull#58): the API passes the app's search to the producer, flags each result for the
/// caller, and answers 503 whenever the producer is down, slow or not set up. The capability flag says beforehand.
/// The producer is a fake handler; the real client, pipeline and controllers run.
/// </summary>
public class SongRequestSearchTest : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
    private const string Key = "search-key";
    private readonly DatabaseFixture _database = new();
    private readonly CapturingLoggerProvider _logs = new();

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private static Dictionary<string, string?> Configured(string baseUrl = "http://producer.test") => new()
    {
        ["Shuffull:SongRequests:BaseUrl"] = baseUrl,
        ["Shuffull:SongRequests:Key"] = Key,
    };

    private Task<ApiTestHost> StartAsync(FakeProducer producer, Dictionary<string, string?> settings, User? signedInAs)
    {
        var coreAssembly = typeof(ValidationBehavior<,>).Assembly;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return ApiTestHost.StartAsync(services =>
        {
            services.AddScoped(_ => _database.CreateContext());
            services.AddValidatorsFromAssemblies([coreAssembly]);
            services.AddMediatR(cfg =>
            {
                cfg.RegisterServicesFromAssemblies(coreAssembly);
                cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            });
            services.AddSingleton<ILoggerProvider>(_logs);
            services.AddSourceSearch(configuration).ConfigurePrimaryHttpMessageHandler(() => producer);
        }, signedInAs);
    }

    private async Task<User> SeedUserAsync(string name)
    {
        var user = new User { UserId = $"user-{name}", Username = name, Version = Now, ServerHash = "server-hash" };
        _database.Context.Users.Add(user);
        await _database.Context.SaveChangesAsync();
        return user;
    }

    private async Task SeedRequestAsync(User user, string externalSongId, SongRequestStatus status)
    {
        _database.Context.SongRequests.Add(new SongRequest
        {
            SongRequestId = $"request-{user.Username}-{externalSongId}", UserId = user.UserId, ExternalSongId = externalSongId,
            Title = "t", TargetPlaylistName = "Requests · week of 2026-11-02", Status = status, CreatedAt = Now, UpdatedAt = Now,
        });
        await _database.Context.SaveChangesAsync();
    }

    private static SourceSearchResultDto Result(string id, string title = "Everybody Wants You") =>
        new(id, title, ["Billy Squier"], "Emotions in Motion", 230, $"https://i.ytimg.com/vi/{id}/hqdefault.jpg", IsExplicit: false);

    private static async Task<string> ErrorOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString()!;

    [Fact]
    public async Task Search_RelaysToTheProducer_AndFlagsWhatTheLibraryHasAndWhatTheCallerAskedFor()
    {
        var user = await SeedUserAsync("a");
        var other = await SeedUserAsync("b");
        _database.Context.Songs.Add(new Song
        {
            SongId = "song-1", Name = "In Library", FileExtension = ".mp3", FileHash = "hash", ExternalSongId = "in-library", Version = Now,
        });
        await _database.Context.SaveChangesAsync();
        await SeedRequestAsync(user, "asked", SongRequestStatus.Pending);
        await SeedRequestAsync(user, "failed", SongRequestStatus.Failed);
        await SeedRequestAsync(user, "cancelled", SongRequestStatus.Cancelled);
        await SeedRequestAsync(other, "someone-else", SongRequestStatus.Pending);

        var producer = FakeProducer.Answering(new List<SourceSearchResultDto>
        {
            Result("in-library"), Result("asked"), Result("failed"), Result("cancelled"), Result("someone-else"),
            Result("asked", "A repeat"), Result("", "No id"), Result("no-title", " "),
        });
        // A base URL with a path: the relay's path goes under it, not in place of it.
        await using var host = await StartAsync(producer, Configured("http://producer.test/funnel"), user);

        var response = await host.Client.GetAsync("/api/v1/song-requests/search?q=%20billy%20squier%20&limit=6");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = (await response.Content.ReadFromJsonAsync<List<SongRequestSearchResultDto>>())!;
        Assert.Equal(
            [("in-library", true, false), ("asked", false, true), ("failed", false, true), ("cancelled", false, false), ("someone-else", false, false)],
            results.Select(r => (r.ExternalSongId, r.InLibrary, r.AlreadyRequested)));
        var first = results[0];
        Assert.Equal(("Everybody Wants You", "Emotions in Motion", 230, "https://i.ytimg.com/vi/in-library/hqdefault.jpg", false),
            (first.Title, first.Album, first.DurationSeconds, first.ThumbnailUrl, first.IsExplicit));
        Assert.Equal(["Billy Squier"], first.Artists);

        var sent = Assert.Single(producer.Requests);
        Assert.Equal(HttpMethod.Get, sent.Method);
        Assert.Equal("http://producer.test/funnel/api/source-search?q=billy%20squier&limit=6", sent.RequestUri!.AbsoluteUri);
        Assert.Equal([Key], sent.Headers.GetValues(SourceSearchClient.KeyHeader));
    }

    [Fact]
    public async Task Search_ReadsAPascalCaseAnswer_AndAMissingArtistListAsEmpty()
    {
        var user = await SeedUserAsync("a");
        var producer = new FakeProducer((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""[{"ExternalSongId":"x1","Title":"Song","IsExplicit":true}]""", Encoding.UTF8, "application/json"),
        }));
        await using var host = await StartAsync(producer, Configured(), user);

        var results = (await host.Client.GetFromJsonAsync<List<SongRequestSearchResultDto>>("/api/v1/song-requests/search?q=song"))!;

        var result = Assert.Single(results);
        Assert.Equal(("x1", "Song", true, 0), (result.ExternalSongId, result.Title, result.IsExplicit, result.Artists.Count));
        Assert.Equal($"limit={Shuffull.Api.Controllers.SongRequestsController.DefaultSearchLimit}",
            producer.Requests.Single().RequestUri!.Query.Split('&')[1]);
    }

    [Fact]
    public async Task Search_WhenTheProducerIsSlow_AnswersSearchUnavailable()
    {
        var user = await SeedUserAsync("a");
        var producer = new FakeProducer(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var settings = Configured();
        settings["Shuffull:SongRequests:Timeout"] = "00:00:00.2";
        await using var host = await StartAsync(producer, settings, user);

        var response = await host.Client.GetAsync("/api/v1/song-requests/search?q=billy");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(SourceSearchClient.UnavailableMessage, await ErrorOf(response));
        Assert.Contains(_logs.Messages, m => m.Contains("took longer than"));
    }

    public static TheoryData<HttpStatusCode, string> Failures => new()
    {
        { HttpStatusCode.InternalServerError, "[]" },
        { HttpStatusCode.Unauthorized, "" },
        { HttpStatusCode.OK, "not json" },
        { HttpStatusCode.OK, "null" },
        { HttpStatusCode.OK, """{"results":[]}""" },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task Search_WhenTheProducerFailsOrAnswersBadly_AnswersSearchUnavailable_WithoutLoggingTheKeyOrText(
        HttpStatusCode status, string body)
    {
        var user = await SeedUserAsync("a");
        var producer = new FakeProducer((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        }));
        await using var host = await StartAsync(producer, Configured(), user);

        var response = await host.Client.GetAsync("/api/v1/song-requests/search?q=secretsongtitle");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(SourceSearchClient.UnavailableMessage, await ErrorOf(response));
        Assert.Contains(_logs.Messages, m => m.StartsWith("Song search failed"));
        Assert.DoesNotContain(_logs.Messages, m => m.Contains(Key) || m.Contains("secretsongtitle"));
    }

    public static TheoryData<string?, string?> MissingConfig => new()
    {
        { null, null },
        { "http://producer.test", null },
        { "http://producer.test", " " },
        { null, Key },
        { "youtubefunnel.web-api:8080", Key },
        { "not a url", Key },
    };

    [Theory]
    [MemberData(nameof(MissingConfig))]
    public async Task WithoutABaseUrlAndKey_TheCapabilityIsFalse_AndSearchIsUnavailable_WithNoCall(string? baseUrl, string? key)
    {
        var user = await SeedUserAsync("a");
        var producer = FakeProducer.Answering(new List<SourceSearchResultDto>());
        await using var host = await StartAsync(producer, new()
        {
            ["Shuffull:SongRequests:BaseUrl"] = baseUrl,
            ["Shuffull:SongRequests:Key"] = key,
        }, user);

        var capabilities = await host.Client.GetFromJsonAsync<CapabilitiesResponse>("/api/v1/capabilities");
        var response = await host.Client.GetAsync("/api/v1/song-requests/search?q=billy");

        Assert.False(capabilities!.SongRequests);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(SourceSearchClient.NotConfiguredMessage, await ErrorOf(response));
        Assert.Empty(producer.Requests);
    }

    [Fact]
    public async Task WithABaseUrlAndKey_TheCapabilityIsTrue()
    {
        var user = await SeedUserAsync("a");
        await using var host = await StartAsync(FakeProducer.Answering(new List<SourceSearchResultDto>()), Configured(), user);

        var capabilities = await host.Client.GetFromJsonAsync<CapabilitiesResponse>("/api/v1/capabilities");

        Assert.True(capabilities!.SongRequests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("q=%20%20")]
    [InlineData("q=billy&limit=0")]
    [InlineData("q=billy&limit=26")]
    public async Task Search_WithNoTextOrALimitOutOfRange_IsA400_WithNoCall(string query)
    {
        var user = await SeedUserAsync("a");
        var producer = FakeProducer.Answering(new List<SourceSearchResultDto>());
        await using var host = await StartAsync(producer, Configured(), user);

        var response = await host.Client.GetAsync($"/api/v1/song-requests/search?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(producer.Requests);
    }

    [Fact]
    public async Task SearchAndCapabilities_NeedASignedInUser()
    {
        var producer = FakeProducer.Answering(new List<SourceSearchResultDto>());
        await using var host = await StartAsync(producer, Configured(), signedInAs: null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/v1/song-requests/search?q=billy")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/v1/capabilities")).StatusCode);
        Assert.Empty(producer.Requests);
    }

    /// <summary>The producer's search endpoint: answers with <c>answer</c> and keeps every request it was sent.</summary>
    private sealed class FakeProducer(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public static FakeProducer Answering(List<SourceSearchResultDto> results) =>
            new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(results) }));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return answer(request, cancellationToken);
        }
    }

    /// <summary>
    /// Keeps what the relay and the HTTP client log. ASP.NET Core's own request log is left out: it records every
    /// request URL, for every endpoint, and is not the relay's to change.
    /// </summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages
        {
            get { lock (_messages) { return _messages.ToList(); } }
        }

        public ILogger CreateLogger(string categoryName) =>
            categoryName.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) ? NullLogger.Instance : new Logger(this);

        public void Dispose()
        {
        }

        private sealed class Logger(CapturingLoggerProvider provider) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (provider._messages)
                {
                    provider._messages.Add(formatter(state, exception));
                }
            }
        }
    }
}
