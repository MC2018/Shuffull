using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Shuffull.Api.Tools.Authorization;

/// <summary>
/// Puts <see cref="AuthorizeAttribute"/> in front of every action that takes a <see cref="FromCurrentUserAttribute"/>
/// parameter and isn't already behind it (on the action or its controller). Asking for the caller is then enough to
/// get the 401 (or 503) for a request without one; forgetting <c>[Authorize]</c> can't let it through.
/// </summary>
public sealed class CurrentUserRequiresSignInConvention : IActionModelConvention
{
    public void Apply(ActionModel action)
    {
        var wantsCaller = action.Parameters.Any(p => p.Attributes.OfType<FromCurrentUserAttribute>().Any());
        if (!wantsCaller
            || action.Filters.OfType<AuthorizeAttribute>().Any()
            || action.Controller.Filters.OfType<AuthorizeAttribute>().Any())
        {
            return;
        }

        action.Filters.Add(new AuthorizeAttribute());
    }
}
