using MediatR;
using Nut.Results;

namespace Shuffull.Core.Features.Users.GetCurrentUser;

public record GetCurrentUserQuery(string UserId) : IRequest<Result<GetCurrentUserResponse>>;
