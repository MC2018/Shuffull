using MediatR;
using Microsoft.Extensions.Logging;
using Nut.Results;
using Shuffull.Core.Authentication;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Persistence.Repositories;
using Shuffull.Core.Persistence.Specifications.Users;

namespace Shuffull.Core.Features.Users.AuthenticateUser;

public class AuthenticateUserHandler(IUnitOfWork unitOfWork, IAuthTokenGenerator tokenGenerator, ILogger<AuthenticateUserHandler> logger)
    : IRequestHandler<AuthenticateUserCommand, Result<AuthSessionResponse>>
{
    public async Task<Result<AuthSessionResponse>> Handle(AuthenticateUserCommand request, CancellationToken cancellationToken)
    {
        // Each user's hash has its own salt, so the stored hash can't be computed up front and matched in the
        // query: load the user(s) by name, then verify. Usernames aren't unique in the schema, hence a list.
        var usersResult = await unitOfWork.Repository<User>()
            .ListAsync(new UserByUsernameSpec(request.Username), cancellationToken);
        if (usersResult.IsError)
        {
            return Result.Error<AuthSessionResponse>(usersResult.GetError().Message);
        }

        var candidates = usersResult.Get();
        if (candidates.Count == 0)
        {
            ServerPasswordHash.VerifyNobody(request.UserHash);
        }

        var user = candidates.FirstOrDefault(candidate => ServerPasswordHash.Verify(candidate, request.UserHash));

        // A missing user and a wrong password are reported identically so the response can't be used
        // to probe which usernames exist.
        if (user == null)
        {
            return Result.Error<AuthSessionResponse>("Username/password is incorrect.");
        }

        if (user.ServerHashSalt == null)
        {
            await UpgradeToOwnSaltAsync(user, request.UserHash, cancellationToken);
        }

        var token = tokenGenerator.Generate(user);

        var dtoResult = UserDto.Create(user);
        if (dtoResult.IsError)
        {
            return Result.Error<AuthSessionResponse>(dtoResult.GetError().Message);
        }

        return Result.Ok(new AuthSessionResponse(dtoResult.Get(), token.Token, token.Expiration));
    }

    /// <summary>
    /// Re-hashes an account made before per-user salts, now that the login has proven the credential. A failed
    /// save doesn't fail the login: the old hash still verifies, so the next login simply tries again.
    /// </summary>
    private async Task UpgradeToOwnSaltAsync(User user, string clientHash, CancellationToken cancellationToken)
    {
        ServerPasswordHash.Set(user, clientHash);

        var saveResult = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saveResult.IsError)
        {
            logger.LogWarning("Could not move user {UserId} to a per-user password salt; will retry at next login: {Error}",
                user.UserId, saveResult.GetError().Message);
        }
    }
}
