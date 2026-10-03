using MediatR;
using Nut.Results;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Persistence.Repositories;
using Shuffull.Core.Persistence.Specifications.UserSongs;

namespace Shuffull.Core.Features.UserSongs.GetUserSongs;

public class GetUserSongsHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetUserSongsQuery, Result<GetUserSongsResponse>>
{
    public async Task<Result<GetUserSongsResponse>> Handle(GetUserSongsQuery request, CancellationToken cancellationToken)
    {
        var userSongsResult = await unitOfWork.Repository<UserSong>()
            .ListAsync(new UserSongsByUserAfterDateSpec(request.UserId, request.AfterDate).AsNoTracking(), cancellationToken);
        if (userSongsResult.IsError)
        {
            return Result.Error<GetUserSongsResponse>(userSongsResult.GetError().Message);
        }

        var userSongs = userSongsResult.Get();

        // The spec fetches one extra row; its presence means another page remains. Drop it so the
        // page itself stays at PageSize.
        var endOfList = userSongs.Count <= UserSongsByUserAfterDateSpec.PageSize;
        var page = endOfList ? userSongs : userSongs.Take(UserSongsByUserAfterDateSpec.PageSize).ToList();

        var dtos = new List<UserSongDto>();
        foreach (var userSong in page)
        {
            var dtoResult = UserSongDto.Create(userSong);
            if (dtoResult.IsError)
            {
                return Result.Error<GetUserSongsResponse>(dtoResult.GetError().Message);
            }

            dtos.Add(dtoResult.Get());
        }

        return Result.Ok(new GetUserSongsResponse(dtos, endOfList));
    }
}
