using MediatR;
using Nut.Results;
using Shuffull.Core.Authentication;
using Shuffull.Core.Behaviors;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Tests.Behaviors;

/// <summary>
/// Verifies the pipeline gate: a [RequiresRole] request only reaches its handler for a user holding the role;
/// an unattributed request is always let through. Mirrors the Sociallite PolicyAuthorizationBehavior test.
/// </summary>
public class RoleAuthorizationBehaviorTest
{
    [RequiresRole(Role.Curator)]
    private record CuratorGatedRequest : IRequest<Result<string>>;

    private record UnrestrictedRequest : IRequest<Result<string>>;

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public User? User { get; init; }
    }

    private static User Curator() => new()
    {
        UserId = "c", Username = "c", Version = DateTime.UtcNow, ServerHash = "h", IsCurator = true,
    };

    private static User NonCurator() => new()
    {
        UserId = "n", Username = "n", Version = DateTime.UtcNow, ServerHash = "h", IsCurator = false,
    };

    private static async Task<(TResponse Response, bool NextCalled)> RunAsync<TRequest, TResponse>(
        TRequest request, ICurrentUser currentUser, TResponse handlerResult)
        where TRequest : IRequest<TResponse>
    {
        var behavior = new RoleAuthorizationBehavior<TRequest, TResponse>(currentUser);
        var nextCalled = false;
        RequestHandlerDelegate<TResponse> next = () =>
        {
            nextCalled = true;
            return Task.FromResult(handlerResult);
        };

        var response = await behavior.Handle(request, next, CancellationToken.None);
        return (response, nextCalled);
    }

    [Fact]
    public async Task GatedRequest_AsCurator_CallsHandler()
    {
        var (response, nextCalled) = await RunAsync(
            new CuratorGatedRequest(), new FakeCurrentUser { User = Curator() }, Result.Ok("handled"));

        Assert.True(nextCalled);
        Assert.True(response.IsOk);
        Assert.Equal("handled", response.Get());
    }

    [Fact]
    public async Task GatedRequest_AsNonCurator_IsForbiddenAndSkipsHandler()
    {
        var (response, nextCalled) = await RunAsync(
            new CuratorGatedRequest(), new FakeCurrentUser { User = NonCurator() }, Result.Ok("handled"));

        Assert.False(nextCalled);
        Assert.True(response.IsError);
        Assert.StartsWith("Forbidden", response.GetError().Message);
    }

    [Fact]
    public async Task GatedRequest_Unauthenticated_IsForbiddenAndSkipsHandler()
    {
        var (response, nextCalled) = await RunAsync(
            new CuratorGatedRequest(), new FakeCurrentUser { User = null }, Result.Ok("handled"));

        Assert.False(nextCalled);
        Assert.True(response.IsError);
        Assert.StartsWith("Forbidden", response.GetError().Message);
    }

    [Fact]
    public async Task UnrestrictedRequest_AlwaysCallsHandler_EvenUnauthenticated()
    {
        var (response, nextCalled) = await RunAsync(
            new UnrestrictedRequest(), new FakeCurrentUser { User = null }, Result.Ok("handled"));

        Assert.True(nextCalled);
        Assert.True(response.IsOk);
    }
}
