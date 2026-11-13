using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Shuffull.Core.Models.Database;

namespace Shuffull.Api.Tools.Authorization;

/// <summary>
/// Runs authorization on the API endpoints
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class AuthorizeAttribute : Attribute, IAuthorizationFilter
{
    /// <summary>
    /// Sets the result to unauthorized if the context hasn't been set, or to 503 when <see cref="JwtMiddleware"/>
    /// couldn't look the user up, so a client can tell "sign in again" from "the server is down".
    /// </summary>
    /// <param name="context"></param>
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (context.HttpContext.Items[JwtMiddleware.UserKey] is User)
        {
            return;
        }

        if (context.HttpContext.Items.ContainsKey(JwtMiddleware.AuthUnavailableKey))
        {
            context.Result = new JsonResult(new { message = "The server can't check your sign-in right now. Try again shortly." })
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable
            };
        }
        else
        {
            context.Result = new JsonResult(new { message = "Unauthorized." }) { StatusCode = StatusCodes.Status401Unauthorized };
        }
    }
}
