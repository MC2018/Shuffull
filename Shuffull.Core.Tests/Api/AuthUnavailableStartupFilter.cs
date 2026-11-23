using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Shuffull.Api.Tools.Authorization;

namespace Shuffull.Core.Tests.Api;

/// <summary>
/// Marks every request the way <see cref="JwtMiddleware"/> does when it can't check a sign-in (the user store is down),
/// so a test can see the 503 that clients answer by retrying rather than signing out.
/// </summary>
internal sealed class AuthUnavailableStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((HttpContext context, Func<Task> nextMiddleware) =>
        {
            context.Items[JwtMiddleware.AuthUnavailableKey] = true;
            return nextMiddleware();
        });
        next(app);
    };
}
