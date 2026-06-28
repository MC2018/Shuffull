using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Authentication;

/// <summary>
/// Abstracts "who is making this request" for the Core pipeline (the role-authorization behavior) without
/// Core taking a dependency on ASP.NET / HttpContext. The API supplies the implementation, which resolves the
/// authenticated <see cref="User"/> the JwtMiddleware already attached to the request.
/// </summary>
public interface ICurrentUser
{
    /// <summary>The authenticated user for the current request, or null when unauthenticated.</summary>
    User? User { get; }
}
