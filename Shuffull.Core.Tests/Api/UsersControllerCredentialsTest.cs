using System.Net.Http.Json;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Nut.Results;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Features.Users;
using Shuffull.Core.Features.Users.AuthenticateUser;
using Shuffull.Core.Features.Users.ChangePassword;
using Shuffull.Core.Features.Users.CreateUser;

namespace Shuffull.Core.Tests.Api;

/// <summary>
/// Credentials come from the JSON body, so they stay out of URLs and therefore out of proxy and access logs. The
/// query string is still read for apps installed before the change.
/// </summary>
public class UsersControllerCredentialsTest
{
    private readonly List<object> _sent = new();

    private Task<ApiTestHost> StartAsync(User? signedInAs = null)
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<IRequest<Result<AuthSessionResponse>>>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<Result<AuthSessionResponse>>, CancellationToken>((request, _) => _sent.Add(request))
            .ReturnsAsync(Result.Error<AuthSessionResponse>("stub"));
        return ApiTestHost.StartAsync(services => services.AddSingleton(mediator.Object), signedInAs);
    }

    [Fact]
    public async Task Authenticate_ReadsCredentialsFromTheJsonBody()
    {
        await using var host = await StartAsync();

        await host.Client.PostAsJsonAsync("/api/v1/users/authenticate", new { username = "a&b +c#", userHash = "hash-1" });

        var command = Assert.IsType<AuthenticateUserCommand>(Assert.Single(_sent));
        Assert.Equal("a&b +c#", command.Username);
        Assert.Equal("hash-1", command.UserHash);
    }

    [Fact]
    public async Task Create_ReadsCredentialsFromTheJsonBody()
    {
        await using var host = await StartAsync();

        await host.Client.PostAsJsonAsync("/api/v1/users", new { username = "alice", userHash = "hash-1" });

        var command = Assert.IsType<CreateUserCommand>(Assert.Single(_sent));
        Assert.Equal("alice", command.Username);
        Assert.Equal("hash-1", command.UserHash);
    }

    [Fact]
    public async Task Authenticate_StillReadsTheQueryStringFromOlderApps()
    {
        await using var host = await StartAsync();

        // What an app from before the change sends: no body at all, credentials in the URL.
        await host.Client.PostAsync("/api/v1/users/authenticate?username=alice&userHash=hash-1", content: null);

        var command = Assert.IsType<AuthenticateUserCommand>(Assert.Single(_sent));
        Assert.Equal("alice", command.Username);
        Assert.Equal("hash-1", command.UserHash);
    }

    [Fact]
    public async Task Create_StillReadsTheQueryStringFromOlderApps()
    {
        await using var host = await StartAsync();

        await host.Client.PostAsync("/api/v1/users?username=alice&userHash=hash-1", content: null);

        var command = Assert.IsType<CreateUserCommand>(Assert.Single(_sent));
        Assert.Equal("alice", command.Username);
        Assert.Equal("hash-1", command.UserHash);
    }

    [Fact]
    public async Task ChangePassword_ReadsBothHashesFromTheBody_ForTheSignedInUser()
    {
        var user = new User { UserId = "user-1", Username = "alice", Version = DateTime.UtcNow, ServerHash = "hash" };
        await using var host = await StartAsync(user);

        await host.Client.PostAsJsonAsync("/api/v1/users/me/password", new { currentUserHash = "old-hash", newUserHash = "new-hash" });

        var command = Assert.IsType<ChangePasswordCommand>(Assert.Single(_sent));
        Assert.Equal("user-1", command.UserId);
        Assert.Equal("old-hash", command.CurrentUserHash);
        Assert.Equal("new-hash", command.NewUserHash);
    }

    [Fact]
    public async Task ChangePassword_WithoutAToken_Is401()
    {
        await using var host = await StartAsync();

        var response = await host.Client.PostAsJsonAsync("/api/v1/users/me/password", new { currentUserHash = "old-hash", newUserHash = "new-hash" });

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_sent);
    }
}
