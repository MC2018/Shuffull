using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Routing;

namespace Shuffull.Api.Tools;

/// <summary>
/// Picks an action by whether a query parameter is present, so two actions with different callers can share one URL.
/// <c>GET song-requests</c> is the app's list without <c>?status=</c> and the producer's queue with it, and each action
/// keeps its own declarative guard (<c>[Authorize]</c> or <c>[ProducerKey]</c>).
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class QueryParameterAttribute(string name, bool present = true) : ActionMethodSelectorAttribute
{
    public override bool IsValidForRequest(RouteContext routeContext, ActionDescriptor action) =>
        routeContext.HttpContext.Request.Query.ContainsKey(name) == present;
}
