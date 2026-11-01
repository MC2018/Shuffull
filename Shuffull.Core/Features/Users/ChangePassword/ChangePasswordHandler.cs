using MediatR;
using Nut.Results;
using Shuffull.Core.Authentication;
using Shuffull.Core.Errors;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Persistence.Repositories;

namespace Shuffull.Core.Features.Users.ChangePassword;

/// <summary>
/// Checks the current password, then stores the new one and a new security stamp in one save, so every token issued
/// before stops working on its next request. Returns a fresh session so the device that made the change stays signed
/// in.
/// </summary>
public class ChangePasswordHandler(IUnitOfWork unitOfWork, IAuthTokenGenerator tokenGenerator)
    : IRequestHandler<ChangePasswordCommand, Result<AuthSessionResponse>>
{
    public async Task<Result<AuthSessionResponse>> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        var userResult = await unitOfWork.Repository<User>().GetByIdAsync(request.UserId, cancellationToken);
        if (userResult.IsError)
        {
            return Result.Error<AuthSessionResponse>(userResult.GetError().NotFoundAs("User not found."));
        }

        var user = userResult.Get();

        // A 400, not a 401: the app logs out on any 401, and a mistyped current password shouldn't sign it out.
        if (!ServerPasswordHash.Verify(user, request.CurrentUserHash))
        {
            return Result.Error<AuthSessionResponse>("Current password is incorrect.");
        }

        ServerPasswordHash.Change(user, request.NewUserHash);

        var saveResult = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saveResult.IsError)
        {
            return Result.Error<AuthSessionResponse>(saveResult.GetError());
        }

        var token = tokenGenerator.Generate(user);

        var dtoResult = UserDto.Create(user);
        if (dtoResult.IsError)
        {
            return Result.Error<AuthSessionResponse>(dtoResult.GetError());
        }

        return Result.Ok(new AuthSessionResponse(dtoResult.Get(), token.Token, token.Expiration));
    }
}
