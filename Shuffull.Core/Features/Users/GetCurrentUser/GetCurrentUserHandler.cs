using MediatR;
using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Persistence.Repositories;

namespace Shuffull.Core.Features.Users.GetCurrentUser;

public class GetCurrentUserHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetCurrentUserQuery, Result<GetCurrentUserResponse>>
{
    public async Task<Result<GetCurrentUserResponse>> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        var userResult = await unitOfWork.Repository<User>().GetByIdAsync(request.UserId, cancellationToken);
        if (userResult.IsError)
        {
            return Result.Error<GetCurrentUserResponse>(userResult.GetError().NotFoundAs("User not found."));
        }

        var dtoResult = UserDto.Create(userResult.Get());
        if (dtoResult.IsError)
        {
            return Result.Error<GetCurrentUserResponse>(dtoResult.GetError());
        }

        return Result.Ok(new GetCurrentUserResponse(dtoResult.Get()));
    }
}
