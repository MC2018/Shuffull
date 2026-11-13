using System.Text;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shuffull.Api.Tools.Authorization;
using Shuffull.Core.Behaviors;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence;
using Shuffull.Core.Services.Alerts;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Api;

/// <summary>
/// The API on a test server with the real MediatR pipeline over a test database, for the endpoints the funnel polls and
/// acks (and the app's replacement flag). Calls go over HTTP, so a test sees the status, body and content type a
/// client gets, whichever layer produces them.
/// </summary>
public static class ProducerEndpointsHost
{
    public const string Key = "import-key";

    /// <summary>The handlers' clock: every timestamp they write is exactly this.</summary>
    public static readonly DateTime Now = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);

    public static Task<ApiTestHost> StartAsync(DatabaseFixture database, IHealthSignals signals, User? signedInAs = null,
        params IInterceptor[] interceptors) => ApiTestHost.StartAsync(services =>
    {
        var coreAssembly = typeof(ValidationBehavior<,>).Assembly;
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [ProducerKeyAttribute.ConfigKey] = Key })
            .Build());
        services.AddScoped<ShuffullContext>(_ => database.CreateContext(interceptors));
        services.AddSingleton(signals);
        services.AddSingleton<TimeProvider>(new ManualTimeProvider(Now));
        services.AddValidatorsFromAssemblies([coreAssembly]);
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblies(coreAssembly);
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });
    }, signedInAs);

    /// <summary>A request the way the funnel sends it: the shared secret in the header, a JSON body if any.</summary>
    public static HttpRequestMessage Producer(HttpMethod method, string path, string? json = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add(ProducerKeyAttribute.HeaderName, Key);
        if (json != null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }
        return request;
    }

    /// <summary>A 400 written by model binding / [ApiController] before the action runs, naming the field it rejected.</summary>
    internal static async Task AssertFrameworkValidation400Async(HttpResponseMessage response, string field)
    {
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.StartsWith("application/problem+json", response.Content.Headers.ContentType?.ToString());
        Assert.Contains($"\"{field}\":[", await response.Content.ReadAsStringAsync());
    }
}
