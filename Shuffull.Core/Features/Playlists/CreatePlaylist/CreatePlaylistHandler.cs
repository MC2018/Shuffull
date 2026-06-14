using MediatR;
using Nut.Results;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Persistence.Repositories;
using Shuffull.Shared.Tools;

namespace Shuffull.Core.Features.Playlists.CreatePlaylist;

public class CreatePlaylistHandler(IUnitOfWork unitOfWork) : IRequestHandler<CreatePlaylistCommand, Result<CreatePlaylistResponse>>
{
    public async Task<Result<CreatePlaylistResponse>> Handle(CreatePlaylistCommand request, CancellationToken cancellationToken)
    {
        var playlist = new Playlist
        {
            PlaylistId = IdGenerator.Generate(),
            UserId = request.UserId,
            Name = request.Name,
            CurrentSongId = null,
            PercentUntilReplayable = 0.9m,
            Version = DateTime.UtcNow,
        };

        var addResult = await unitOfWork.Repository<Playlist>().AddAsync(playlist, cancellationToken);
        if (addResult.IsError)
        {
            return Result.Error<CreatePlaylistResponse>(addResult.GetError().Message);
        }

        var saveResult = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saveResult.IsError)
        {
            return Result.Error<CreatePlaylistResponse>(saveResult.GetError().Message);
        }

        var dtoResult = PlaylistDto.Create(playlist);
        if (dtoResult.IsError)
        {
            return Result.Error<CreatePlaylistResponse>(dtoResult.GetError().Message);
        }

        return Result.Ok(new CreatePlaylistResponse(dtoResult.Get()));
    }
}
