using Microsoft.AspNetCore.Mvc;
using Nut.Results;
using Shuffull.Api.Extensions;

namespace Shuffull.Core.Tests.Tools;

public class ControllerBaseExtensionsTest
{
    // Minimal concrete controller; the extension methods only need ControllerBase's Ok/BadRequest helpers.
    private sealed class TestController : ControllerBase
    {
    }

    private readonly TestController _controller = new();

    [Fact]
    public void ToActionResult_OkResultOfT_ReturnsOkWithValue()
    {
        var action = _controller.ToActionResult(Result.Ok("payload"));

        var ok = Assert.IsType<OkObjectResult>(action);
        Assert.Equal("payload", ok.Value);
    }

    [Fact]
    public void ToActionResult_ErrorResultOfT_ReturnsBadRequestWithErrorPayload()
    {
        var action = _controller.ToActionResult(Result.Error<string>("boom"));

        var badRequest = Assert.IsType<BadRequestObjectResult>(action);
        // The payload is an anonymous { error = message }; read the property reflectively.
        var errorProperty = badRequest.Value!.GetType().GetProperty("error");
        Assert.NotNull(errorProperty);
        Assert.Equal("boom", errorProperty!.GetValue(badRequest.Value));
    }

    [Fact]
    public void ToActionResult_OkResult_ReturnsOk()
    {
        var action = _controller.ToActionResult(Result.Ok());

        Assert.IsType<OkResult>(action);
    }

    [Fact]
    public void ToActionResult_ErrorResult_ReturnsBadRequestWithErrorPayload()
    {
        var action = _controller.ToActionResult(Result.Error("nope"));

        var badRequest = Assert.IsType<BadRequestObjectResult>(action);
        var errorProperty = badRequest.Value!.GetType().GetProperty("error");
        Assert.NotNull(errorProperty);
        Assert.Equal("nope", errorProperty!.GetValue(badRequest.Value));
    }
}
