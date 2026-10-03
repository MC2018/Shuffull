using Shuffull.Core.Models.Dtos;

namespace Shuffull.Core.Features.Users;

/// <summary>
/// The result of establishing a session — used by both authentication and registration since each
/// returns the user plus a freshly issued token. Mirrors the legacy
/// <c>Shuffull.Shared.Models.Responses.AuthenticateResponse</c>.
/// </summary>
public record AuthSessionResponse(UserDto User, string Token, DateTime Expiration);
