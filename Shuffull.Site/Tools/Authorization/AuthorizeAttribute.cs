using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Shuffull.Site.Models.Database;

namespace Shuffull.Site.Tools.Authorization
{
    /// <summary>
    /// Runs authorization on the API endpoints
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class AuthorizeAttribute : Attribute, IAuthorizationFilter
    {
        /// <summary>
        /// Sets the result to unauthorized if the context hasn't been set
        /// </summary>
        /// <param name="context"></param>
        public void OnAuthorization(AuthorizationFilterContext context)
        {
            if (context.HttpContext.Items["User"] is not User)
            {
                context.Result = new JsonResult(new { message = "Unauthorized." }) { StatusCode = StatusCodes.Status401Unauthorized };
            }
            else if (false /* use for when permissions are implemented */)
            {
                context.Result = new JsonResult(new { message = "Forbidden." }) { StatusCode = StatusCodes.Status403Forbidden };
            }
        }
    }
}
