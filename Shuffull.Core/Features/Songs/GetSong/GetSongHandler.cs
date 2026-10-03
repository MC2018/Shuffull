using MediatR;
using Nut.Results;
using Shuffull.Core.Persistence.Repositories;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Persistence.Specifications.Songs;

namespace Shuffull.Core.Features.Songs.GetSong;

public class GetSongHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetSongQuery, Result<GetSongResponse>>
{
    public async Task<Result<GetSongResponse>> Handle(GetSongQuery request, CancellationToken cancellationToken)
    {
        var songRepository = unitOfWork.Repository<Song>();
        var songResult = await songRepository.GetAsync(new SongByIdSpec(request.SongId).AsNoTracking(), cancellationToken);
        if (songResult.IsError)
        {
            return Result.Error<GetSongResponse>($"Song with ID {request.SongId} not found.");
        }

        var songDtoResult = SongDto.Create(songResult.Get());
        if (songDtoResult.IsError)
        {
            return Result.Error<GetSongResponse>(songDtoResult.GetError().Message);
        }

        return Result.Ok(new GetSongResponse(songDtoResult.Get()));
    }
}
