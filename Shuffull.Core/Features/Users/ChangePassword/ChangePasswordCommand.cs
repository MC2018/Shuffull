using MediatR;
using Nut.Results;

namespace Shuffull.Core.Features.Users.ChangePassword;

/// <summary>
/// Changes the signed-in user's password. Both hashes are the client's argon2 of "username;password", the same value
/// login sends, so the raw passwords never reach the server.
/// </summary>
public record ChangePasswordCommand(string UserId, string CurrentUserHash, string NewUserHash)
    : IRequest<Result<AuthSessionResponse>>;
