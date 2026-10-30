using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Features.Users;

namespace Shuffull.Core.Tests.Api;

/// <summary>
/// The status each error kind is answered with (Shuffull#41). The installed app's outbox keeps a 4xx for a while and
/// then dead-letters it, but retries a 5xx, so a server fault must not come back as a 400. The body keeps its shape.
/// </summary>
public class ErrorStatusTest
{
    private static Task<ApiTestHost> StartAsync(Exception error)
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<IRequest<Result<AuthSessionResponse>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Error<AuthSessionResponse>(error));
        return ApiTestHost.StartAsync(services => services.AddSingleton(mediator.Object));
    }

    public static TheoryData<Exception, HttpStatusCode> Cases => new()
    {
        { new Exception("plain"), HttpStatusCode.BadRequest },
        { new KindedError(ErrorKind.Invalid, "invalid"), HttpStatusCode.BadRequest },
        { ResultErrors.Forbidden("forbidden"), HttpStatusCode.Forbidden },
        { ResultErrors.NotFound("missing"), HttpStatusCode.NotFound },
        { ResultErrors.Conflict("taken"), HttpStatusCode.Conflict },
        { new KindedError(ErrorKind.ServerFault, "db down"), HttpStatusCode.InternalServerError },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ErrorKind_DecidesTheStatus_AndTheBodyIsStillAnErrorMessage(Exception error, HttpStatusCode expected)
    {
        await using var host = await StartAsync(error);

        var response = await host.Client.PostAsJsonAsync("/api/v1/users/authenticate", new { username = "a", userHash = "h" });

        Assert.Equal(expected, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(error.Message, body.GetProperty("error").GetString());
    }
}
