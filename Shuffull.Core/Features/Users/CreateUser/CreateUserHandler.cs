using MediatR;
using Nut.Results;
using Shuffull.Core.Authentication;
using Shuffull.Core.Features.Users;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Persistence.Repositories;
using Shuffull.Core.Persistence.Specifications.Users;
using Shuffull.Shared.Tools;

namespace Shuffull.Core.Features.Users.CreateUser;

public class CreateUserHandler(IUnitOfWork unitOfWork, IAuthTokenGenerator tokenGenerator)
    : IRequestHandler<CreateUserCommand, Result<AuthSessionResponse>>
{
    public async Task<Result<AuthSessionResponse>> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        var userRepository = unitOfWork.Repository<User>();
        var existsResult = await userRepository
            .AnyAsync(new UserByUsernameSpec(request.Username).AsNoTracking(), cancellationToken);
        if (existsResult.IsError)
        {
            return Result.Error<AuthSessionResponse>(existsResult.GetError().Message);
        }

        if (existsResult.Get())
        {
            return Result.Error<AuthSessionResponse>($"User '{request.Username}' already exists.");
        }

        var user = new User
        {
            UserId = IdGenerator.Generate(),
            Username = request.Username,
            ServerHash = Hasher.Argon2Hash(request.UserHash),
            Version = DateTime.UtcNow,
        };

        var addResult = await userRepository.AddAsync(user, cancellationToken);
        if (addResult.IsError)
        {
            return Result.Error<AuthSessionResponse>(addResult.GetError().Message);
        }

        var saveResult = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saveResult.IsError)
        {
            return Result.Error<AuthSessionResponse>(saveResult.GetError().Message);
        }

        var token = tokenGenerator.Generate(user);

        var dtoResult = UserDto.Create(user);
        if (dtoResult.IsError)
        {
            return Result.Error<AuthSessionResponse>(dtoResult.GetError().Message);
        }

        return Result.Ok(new AuthSessionResponse(dtoResult.Get(), token.Token, token.Expiration));
    }
}
