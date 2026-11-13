using Shuffull.Core.Authentication;
using Shuffull.Core.Models.Database;

namespace Shuffull.Api.Tools.Authorization;

/// <summary>
/// Resolves the authenticated <see cref="User"/> for the Core pipeline from the request, where
/// <see cref="JwtMiddleware"/> already attached it under <see cref="JwtMiddleware.UserKey"/> (read live from the DB
/// on each request). Returns null when there is no HTTP context or the request is unauthenticated.
/// </summary>
public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public User? User => _httpContextAccessor.HttpContext?.Items[JwtMiddleware.UserKey] as User;
}
