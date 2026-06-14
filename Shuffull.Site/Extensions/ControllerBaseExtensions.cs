using Microsoft.AspNetCore.Mvc;
using Nut.Results;

namespace Shuffull.Site.Extensions;

/// <summary>
/// Maps a <see cref="Result"/> / <see cref="Result{T}"/> onto an HTTP response so controllers stay
/// thin: success becomes 200 (with the value), failure becomes 400 with an { error } payload.
/// </summary>
public static class ControllerBaseExtensions
{
    public static IActionResult ToActionResult<T>(this ControllerBase controller, Result<T> result)
        => result.IsOk
            ? controller.Ok(result.Get())
            : controller.BadRequest(new { error = result.GetError().Message });

    public static IActionResult ToActionResult(this ControllerBase controller, Result result)
        => result.IsOk
            ? controller.Ok()
            : controller.BadRequest(new { error = result.GetError().Message });
}
