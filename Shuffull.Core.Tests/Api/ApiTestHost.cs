using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shuffull.Api.Controllers;
using Shuffull.Api.Extensions;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Tests.Api;

/// <summary>
/// The API's real controllers on an in-memory test server, with only the services a test registers. Lets a test
/// see what model binding and the auth filter actually do with a request, which a handler test can't.
/// </summary>
public sealed class ApiTestHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    public HttpClient Client { get; }

    private ApiTestHost(WebApplication app)
    {
        _app = app;
        Client = app.GetTestClient();
    }

    /// <param name="signedInAs">Attached the way JwtMiddleware does for a valid token; null = no token.</param>
    public static async Task<ApiTestHost> StartAsync(Action<IServiceCollection> configureServices, User? signedInAs = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddShuffullControllers().AddApplicationPart(typeof(UsersController).Assembly);
        configureServices(builder.Services);

        var app = builder.Build();
        if (signedInAs != null)
        {
            app.Use((HttpContext context, Func<Task> next) =>
            {
                context.Items["User"] = signedInAs;
                return next();
            });
        }
        app.MapControllers();
        await app.StartAsync();
        return new ApiTestHost(app);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync();
    }
}
