using Microsoft.AspNetCore.Mvc;
using Nut.Results;
using Shuffull.Core.Errors;

namespace Shuffull.Api.Extensions;

/// <summary>
/// Maps a <see cref="Result"/> / <see cref="Result{T}"/> onto an HTTP response so controllers stay
/// thin: success becomes 200 (with the value), failure becomes an { error } payload whose status comes from the
/// error's <see cref="ErrorKind"/>: 400, 403, 404, 409 or 500. An error without a kind is a 400.
/// </summary>
public static class ControllerBaseExtensions
{
    public static IActionResult ToActionResult<T>(this ControllerBase controller, Result<T> result)
        => result.IsOk
            ? controller.Ok(result.Get())
            : controller.Failure(result.GetError());

    public static IActionResult ToActionResult(this ControllerBase controller, Result result)
        => result.IsOk
            ? controller.Ok()
            : controller.Failure(result.GetError());

    private static IActionResult Failure(this ControllerBase controller, Exception error)
    {
        var body = new { error = error.Message };
        switch (ResultErrors.KindOf(error))
        {
            case ErrorKind.Forbidden:
                return controller.StatusCode(StatusCodes.Status403Forbidden, body);
            case ErrorKind.NotFound:
                return controller.NotFound(body);
            case ErrorKind.Conflict:
                return controller.Conflict(body);
            case ErrorKind.ServerFault:
                // Returned, not thrown, so nothing else logs it. A query cancelled because the caller hung up isn't a
                // fault worth an error entry; nobody receives this answer anyway.
                if (!controller.HttpContext.RequestAborted.IsCancellationRequested)
                {
                    controller.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                        .CreateLogger(typeof(ControllerBaseExtensions))
                        .LogError(error, "{Method} {Path} failed on the server: {Error}",
                            controller.Request.Method, controller.Request.Path, error.Message);
                }
                return controller.StatusCode(StatusCodes.Status500InternalServerError, body);
            default:
                return controller.BadRequest(body);
        }
    }
}
