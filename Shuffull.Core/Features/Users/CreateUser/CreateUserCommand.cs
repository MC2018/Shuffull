using MediatR;
using Nut.Results;
using Shuffull.Core.Features.Users;

namespace Shuffull.Core.Features.Users.CreateUser;

/// <summary>
/// Registers a new user. <paramref name="UserHash"/> is the client-side hash of the password; the
/// handler applies the server-side hash before storing it.
/// </summary>
public record CreateUserCommand(string Username, string UserHash)
    : IRequest<Result<AuthSessionResponse>>;
