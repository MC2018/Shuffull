using MediatR;
using Nut.Results;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Persistence.Repositories;
using Shuffull.Core.Persistence.Specifications.Songs;
using Shuffull.Core.Persistence.Specifications.UserSongs;

namespace Shuffull.Core.Features.UserSongs.CreateUserSongs;

public class CreateUserSongsHandler(IUnitOfWork unitOfWork) : IRequestHandler<CreateUserSongsCommand, Result<CreateUserSongsResponse>>
{
    public async Task<Result<CreateUserSongsResponse>> Handle(CreateUserSongsCommand request, CancellationToken cancellationToken)
    {
        // Only create records for songs that actually exist, mirroring the legacy controller which
        // intersected the requested ids with the Songs table before inserting.
        var songsResult = await unitOfWork.Repository<Song>()
            .ListAsync(new SongsByIdsSpec(request.SongIds.ToList()).AsNoTracking(), cancellationToken);
        if (songsResult.IsError)
        {
            return Result.Error<CreateUserSongsResponse>(songsResult.GetError().Message);
        }

        var userSongRepository = unitOfWork.Repository<UserSong>();
        var existingResult = await userSongRepository
            .ListAsync(new UserSongsByUserAndSongIdsSpec(request.UserId, request.SongIds.ToList()).AsNoTracking(), cancellationToken);
        if (existingResult.IsError)
        {
            return Result.Error<CreateUserSongsResponse>(existingResult.GetError().Message);
        }

        var existingSongIds = existingResult.Get().Select(us => us.SongId).ToHashSet();
        var now = DateTime.UtcNow;
        var created = new List<UserSong>();

        foreach (var song in songsResult.Get())
        {
            if (existingSongIds.Contains(song.SongId))
            {
                continue;
            }

            var userSong = new UserSong
            {
                UserId = request.UserId,
                SongId = song.SongId,
                Version = now,
            };

            var addResult = await userSongRepository.AddAsync(userSong, cancellationToken);
            if (addResult.IsError)
            {
                return Result.Error<CreateUserSongsResponse>(addResult.GetError().Message);
            }

            created.Add(userSong);
        }

        if (created.Count > 0)
        {
            var saveResult = await unitOfWork.SaveChangesAsync(cancellationToken);
            if (saveResult.IsError)
            {
                return Result.Error<CreateUserSongsResponse>(saveResult.GetError().Message);
            }
        }

        var dtos = new List<UserSongDto>();
        foreach (var userSong in created)
        {
            var dtoResult = UserSongDto.Create(userSong);
            if (dtoResult.IsError)
            {
                return Result.Error<CreateUserSongsResponse>(dtoResult.GetError().Message);
            }

            dtos.Add(dtoResult.Get());
        }

        return Result.Ok(new CreateUserSongsResponse(dtos));
    }
}
