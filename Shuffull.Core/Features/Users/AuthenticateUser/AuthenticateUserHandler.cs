using MediatR;
using Nut.Results;
using Shuffull.Core.Authentication;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Persistence.Repositories;
using Shuffull.Core.Persistence.Specifications.Users;
using Shuffull.Shared.Tools;

namespace Shuffull.Core.Features.Users.AuthenticateUser;

public class AuthenticateUserHandler(IUnitOfWork unitOfWork, IAuthTokenGenerator tokenGenerator)
    : IRequestHandler<AuthenticateUserCommand, Result<AuthSessionResponse>>
{
    public async Task<Result<AuthSessionResponse>> Handle(AuthenticateUserCommand request, CancellationToken cancellationToken)
    {
        var serverHash = Hasher.Argon2Hash(request.UserHash);
        var userResult = await unitOfWork.Repository<User>()
            .GetAsync(new UserByCredentialsSpec(request.Username, serverHash).AsNoTracking(), cancellationToken);

        // A missing user and a wrong password are reported identically so the response can't be used
        // to probe which usernames exist.
        if (userResult.IsError)
        {
            return Result.Error<AuthSessionResponse>("Username/password is incorrect.");
        }

        var user = userResult.Get();
        var token = tokenGenerator.Generate(user);

        var dtoResult = UserDto.Create(user);
        if (dtoResult.IsError)
        {
            return Result.Error<AuthSessionResponse>(dtoResult.GetError().Message);
        }

        return Result.Ok(new AuthSessionResponse(dtoResult.Get(), token.Token, token.Expiration));
    }
}
