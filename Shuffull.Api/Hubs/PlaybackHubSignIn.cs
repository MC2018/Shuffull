using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Shuffull.Api.Tools.Authorization;
using Shuffull.Core.Features.Playback;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence;

namespace Shuffull.Api.Hubs;

/// <summary>
/// The sign-in check for <see cref="PlaybackHub.Path"/>, run on every request to it (negotiate, the WebSocket upgrade,
/// each long poll), after <see cref="JwtMiddleware"/>. The custom [Authorize] filter is MVC-only and doesn't cover a
/// hub, so this is the hub's gate: no signed-in user is a 401 (503 if the sign-in couldn't be checked), and a
/// <c>deviceId</c> that isn't one of the user's checked-in devices is a 403. A request that passes gets an
/// <see cref="HttpContext.User"/> naming the user (SignalR's user id) and the device, which the hub reads.
/// </summary>
public class PlaybackHubSignIn(RequestDelegate next)
{
    public async Task Invoke(HttpContext httpContext, ShuffullContext dbContext)
    {
        if (!httpContext.Request.Path.StartsWithSegments(PlaybackHub.Path))
        {
            await next(httpContext);
            return;
        }

        if (httpContext.Items[JwtMiddleware.UserKey] is not User user)
        {
            var unavailable = httpContext.Items.ContainsKey(JwtMiddleware.AuthUnavailableKey);
            await Refuse(httpContext,
                unavailable ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status401Unauthorized,
                unavailable ? "The server can't check your sign-in right now. Try again shortly." : "Unauthorized.");
            return;
        }

        string? deviceId = httpContext.Request.Query["deviceId"];
        if (string.IsNullOrEmpty(deviceId) || deviceId.Length > UserDevice.MaxDeviceIdLength || !PlaybackRules.NoControlCharacters(deviceId))
        {
            await Refuse(httpContext, StatusCodes.Status400BadRequest, "Connect with ?deviceId= set to this device's id.");
            return;
        }

        var theirs = await dbContext.UserDevices.AsNoTracking()
            .AnyAsync(d => d.UserId == user.UserId && d.DeviceId == deviceId, httpContext.RequestAborted);
        if (!theirs)
        {
            await Refuse(httpContext, StatusCodes.Status403Forbidden, "That device isn't one of your checked-in devices.");
            return;
        }

        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.UserId), new Claim(PlaybackHub.DeviceClaim, deviceId)],
            authenticationType: "ShuffullJwt"));
        await next(httpContext);
    }

    private static Task Refuse(HttpContext httpContext, int status, string message)
    {
        httpContext.Response.StatusCode = status;
        return httpContext.Response.WriteAsJsonAsync(new { message });
    }
}
