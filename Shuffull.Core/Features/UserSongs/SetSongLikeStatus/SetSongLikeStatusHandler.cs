using MediatR;
using Nut.Results;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence.Repositories;
using Shuffull.Core.Persistence.Specifications.UserSongs;

namespace Shuffull.Core.Features.UserSongs.SetSongLikeStatus;

public class SetSongLikeStatusHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<SetSongLikeStatusCommand, Result<SetSongLikeStatusResponse>>
{
    public async Task<Result<SetSongLikeStatusResponse>> Handle(SetSongLikeStatusCommand request, CancellationToken cancellationToken)
    {
        // Tracked load (no AsNoTracking) so the edit below is persisted by the unit of work's change tracker.
        var userSongRepository = unitOfWork.Repository<UserSong>();
        var userSongsResult = await userSongRepository
            .ListAsync(new UserSongsByUserAndSongIdsSpec(request.UserId, [request.SongId]), cancellationToken);
        if (userSongsResult.IsError)
        {
            return Result.Error<SetSongLikeStatusResponse>(userSongsResult.GetError().Message);
        }

        var userSong = userSongsResult.Get().FirstOrDefault();
        if (userSong is null)
        {
            return Result.Error<SetSongLikeStatusResponse>("No matching song was found for this user.");
        }

        if (userSong.LikeStatus != request.LikeStatus)
        {
            var now = DateTime.UtcNow;
            userSong.LikeStatus = request.LikeStatus;
            userSong.Version = now;

            // Bump the user's version so clients pick up the change on their next sync.
            var userResult = await unitOfWork.Repository<User>().GetByIdAsync(request.UserId, cancellationToken);
            if (userResult.IsError)
            {
                return Result.Error<SetSongLikeStatusResponse>("User not found.");
            }
            userResult.Get().Version = now;

            var saveResult = await unitOfWork.SaveChangesAsync(cancellationToken);
            if (saveResult.IsError)
            {
                return Result.Error<SetSongLikeStatusResponse>(saveResult.GetError().Message);
            }
        }

        return Result.Ok(new SetSongLikeStatusResponse(request.SongId, userSong.LikeStatus));
    }
}
