using MediatR;
using Nut.Results;

namespace Shuffull.Core.Features.Users.AuthenticateUser;

/// <summary>
/// Authenticates a user. <paramref name="UserHash"/> is the client-side hash of the password; the
/// handler applies the server-side hash before comparing, so the raw password never reaches here.
/// </summary>
public record AuthenticateUserCommand(string Username, string UserHash)
    : IRequest<Result<AuthSessionResponse>>;
