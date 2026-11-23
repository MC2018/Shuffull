using MediatR;
using Nut.Results;
using Shuffull.Core.Features.SongMerges;
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
        var requested = request.SongIds.Distinct().ToList();
        var songsResult = await unitOfWork.Repository<Song>()
            .ListAsync(new SongsByIdsSpec(requested).AsNoTracking(), cancellationToken);
        if (songsResult.IsError)
        {
            return Result.Error<CreateUserSongsResponse>(songsResult.GetError());
        }
        var songs = songsResult.Get();

        // A song merged away after the device queued the add: the song it became goes into the library instead.
        var missing = requested.Except(songs.Select(s => s.SongId)).ToList();
        if (missing.Count > 0)
        {
            var followed = await SongMergeAliases.FollowMergesAsync(unitOfWork, missing, cancellationToken);
            if (followed.IsError)
            {
                return Result.Error<CreateUserSongsResponse>(followed.GetError());
            }
            var survivorIds = followed.Get().Values.Except(songs.Select(s => s.SongId)).Distinct().ToList();
            if (survivorIds.Count > 0)
            {
                var survivorsResult = await unitOfWork.Repository<Song>()
                    .ListAsync(new SongsByIdsSpec(survivorIds).AsNoTracking(), cancellationToken);
                if (survivorsResult.IsError)
                {
                    return Result.Error<CreateUserSongsResponse>(survivorsResult.GetError());
                }
                songs.AddRange(survivorsResult.Get());
            }
        }
        var songIds = songs.Select(s => s.SongId).ToList();

        var userSongRepository = unitOfWork.Repository<UserSong>();
        var existingResult = await userSongRepository
            .ListAsync(new UserSongsByUserAndSongIdsSpec(request.UserId, songIds).AsNoTracking(), cancellationToken);
        if (existingResult.IsError)
        {
            return Result.Error<CreateUserSongsResponse>(existingResult.GetError());
        }

        var existingSongIds = existingResult.Get().Select(us => us.SongId).ToHashSet();
        var now = DateTime.UtcNow;
        var created = new List<UserSong>();

        foreach (var song in songs)
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
                return Result.Error<CreateUserSongsResponse>(addResult.GetError());
            }

            created.Add(userSong);
        }

        if (created.Count > 0)
        {
            var saveResult = await unitOfWork.SaveChangesAsync(cancellationToken);
            if (saveResult.IsError)
            {
                return Result.Error<CreateUserSongsResponse>(saveResult.GetError());
            }
        }

        var dtos = new List<UserSongDto>();
        foreach (var userSong in created)
        {
            var dtoResult = UserSongDto.Create(userSong);
            if (dtoResult.IsError)
            {
                return Result.Error<CreateUserSongsResponse>(dtoResult.GetError());
            }

            dtos.Add(dtoResult.Get());
        }

        return Result.Ok(new CreateUserSongsResponse(dtos));
    }
}
