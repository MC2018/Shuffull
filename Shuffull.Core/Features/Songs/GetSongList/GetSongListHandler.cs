using MediatR;
using Nut.Results;
using Shuffull.Core.Persistence.Repositories;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Persistence.Specifications.Songs;

namespace Shuffull.Core.Features.Songs.GetSongList;

public class GetSongListHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetSongListQuery, Result<GetSongListResponse>>
{
    public async Task<Result<GetSongListResponse>> Handle(GetSongListQuery request, CancellationToken cancellationToken)
    {
        var songRepository = unitOfWork.Repository<Song>();
        var songsResult = await songRepository.ListAsync(new SongsByIdsSpec(request.SongIds).AsNoTracking(), cancellationToken);
        if (songsResult.IsError)
        {
            return Result.Error<GetSongListResponse>(songsResult.GetError().Message);
        }

        var dtos = new List<SongDto>();
        foreach (var song in songsResult.Get())
        {
            var dtoResult = SongDto.Create(song);
            if (dtoResult.IsError)
            {
                return Result.Error<GetSongListResponse>(dtoResult.GetError().Message);
            }

            dtos.Add(dtoResult.Get());
        }

        return Result.Ok(new GetSongListResponse(dtos));
    }
}
