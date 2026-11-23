using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shuffull.Api.Controllers;
using Shuffull.Api.Tools.Authorization;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Tests.Api;

/// <summary>
/// <c>[FromCurrentUser] User user</c> is how an action gets the caller (Shuffull#32). Asking for it is enough to be
/// signed-in only: no token is the same 401 body the [Authorize] filter always sent, and a sign-in the server couldn't
/// check is the same 503, on every endpoint that takes the caller and on an action that forgot [Authorize].
/// </summary>
public class CurrentUserBindingTest
{
    private static Task<ApiTestHost> StartAsync(User? signedInAs = null, bool authUnavailable = false) =>
        ApiTestHost.StartAsync(services =>
        {
            services.AddControllers().AddApplicationPart(typeof(CallerProbeController).Assembly);
            if (authUnavailable)
            {
                services.AddTransient<IStartupFilter, AuthUnavailableStartupFilter>();
            }
        }, signedInAs);

    /// <summary>Every API action that takes the caller, as a request that reaches it.</summary>
    private static List<(string Method, string Path, string Action)> CallerEndpoints(ApiTestHost host) =>
        host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => (Endpoint: e, Action: e.Metadata.GetMetadata<ControllerActionDescriptor>()))
            .Where(x => x.Action != null
                        && x.Action.ControllerTypeInfo.Assembly == typeof(UsersController).Assembly
                        && x.Action.MethodInfo.GetParameters().Any(p => p.GetCustomAttribute<FromCurrentUserAttribute>() != null))
            .SelectMany(x => x.Endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods
                .Select(method => (method, "/" + FillRoute(x.Endpoint.RoutePattern.RawText!), x.Action!.DisplayName!)))
            .ToList();

    private static string FillRoute(string template) =>
        System.Text.RegularExpressions.Regex.Replace(template, @"\{(\w+)(:int)?\}", m => m.Groups[2].Success ? "2026" : "x");

    private static async Task<string> SendAsync(ApiTestHost host, string method, string path, HttpStatusCode expected)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "POST" or "PUT")
        {
            request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        }

        var response = await host.Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(expected == response.StatusCode, $"{method} {path}: expected {(int)expected}, got {(int)response.StatusCode} {body}");
        return body;
    }

    [Fact]
    public async Task EveryCallerEndpoint_NoToken_Is401WithTheFilterBody()
    {
        await using var host = await StartAsync();

        var endpoints = CallerEndpoints(host);

        // The 22 actions that read the caller by hand before Shuffull#32. More is fine; fewer means one was missed.
        Assert.True(endpoints.Count >= 22, $"Only {endpoints.Count} endpoints take [FromCurrentUser].");
        foreach (var (method, path, _) in endpoints)
        {
            var body = await SendAsync(host, method, path, HttpStatusCode.Unauthorized);
            Assert.Equal("Unauthorized.", JsonNode.Parse(body)!["message"]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task EveryCallerEndpoint_SignInCantBeChecked_Is503()
    {
        await using var host = await StartAsync(authUnavailable: true);

        foreach (var (method, path, _) in CallerEndpoints(host))
        {
            await SendAsync(host, method, path, HttpStatusCode.ServiceUnavailable);
        }
    }

    [Fact]
    public async Task ActionWithoutAuthorize_NoToken_Is401()
    {
        await using var host = await StartAsync();

        var body = await SendAsync(host, "GET", "/test/caller", HttpStatusCode.Unauthorized);

        Assert.Equal("Unauthorized.", JsonNode.Parse(body)!["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task ActionWithoutAuthorize_SignInCantBeChecked_Is503()
    {
        await using var host = await StartAsync(authUnavailable: true);

        await SendAsync(host, "GET", "/test/caller", HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task SignedIn_BindsTheAttachedUser()
    {
        // ServerHash left null on purpose: the row's annotations must not become a 400 from [ApiController].
        var me = new User { UserId = "user-1", Username = "max", Version = DateTime.UtcNow };
        await using var host = await StartAsync(signedInAs: me);

        var body = await SendAsync(host, "GET", "/test/caller", HttpStatusCode.OK);

        Assert.Equal("max", body);
    }
}

/// <summary>Takes the caller but carries no [Authorize]: the convention must still guard it.</summary>
[ApiController]
[Route("test/caller")]
public class CallerProbeController : ControllerBase
{
    [HttpGet]
    public string Get([FromCurrentUser] User user) => user.Username;
}
