using System.Net;
using System.Text.Json.Nodes;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Shuffull.Core.Behaviors;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence;
using Shuffull.Core.Tests.Features.Listens.GetRecap;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Api;

/// <summary>
/// <c>GET /api/v1/recap/{year}</c> through the real controller, auth filter and validation pipeline: no token is a 401
/// like the rest of the API, and a zone the server doesn't know is a 400, not a recap silently computed in UTC.
/// </summary>
public class RecapControllerTest : IDisposable
{
    private readonly DatabaseFixture _database = new();
    private readonly User _me = RecapFixture.NewUser();

    public RecapControllerTest()
    {
        _database.Context.Users.Add(_me);
        new RecapFixture(_database.Context, _me.UserId).Listen("song", RecapFixture.Utc(2026, 1, 1, 4, 30), minutes: 2);
        _database.Context.SaveChanges();
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private Task<ApiTestHost> StartAsync(User? signedInAs) => ApiTestHost.StartAsync(services =>
    {
        var coreAssembly = typeof(ValidationBehavior<,>).Assembly;
        services.AddScoped<ShuffullContext>(_ => _database.CreateContext());
        services.AddValidatorsFromAssemblies([coreAssembly]);
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblies(coreAssembly);
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });
    }, signedInAs);

    [Fact]
    public async Task NoToken_Is401()
    {
        await using var host = await StartAsync(signedInAs: null);

        var response = await host.Client.GetAsync("api/v1/recap/2026");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("api/v1/recap/2026?timeZone=Mars/Olympus_Mons")]
    [InlineData("api/v1/recap/2026?timeZone=%2B15:00")]
    [InlineData("api/v1/recap/1999")]
    public async Task UnknownZoneOrYear_Is400(string url)
    {
        await using var host = await StartAsync(_me);

        var response = await host.Client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(JsonNode.Parse(await response.Content.ReadAsStringAsync())?["error"]);
    }

    [Theory]
    // 04:30 UTC on 1 January is still 2025 in New York, and 2026 everywhere east of it.
    [InlineData("api/v1/recap/2026?timeZone=America/New_York", "America/New_York", 0)]
    [InlineData("api/v1/recap/2026?timeZone=%2B02:00", "+02:00", 2)]
    [InlineData("api/v1/recap/2026", "UTC", 2)]
    public async Task KnownZone_IsTheCallersRecapInThatZone(string url, string timeZone, double totalMinutes)
    {
        await using var host = await StartAsync(_me);

        var response = await host.Client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal((2026, timeZone, totalMinutes),
            (body["year"]!.GetValue<int>(), body["timeZone"]!.GetValue<string>(), body["totalMinutes"]!.GetValue<double>()));
    }
}
