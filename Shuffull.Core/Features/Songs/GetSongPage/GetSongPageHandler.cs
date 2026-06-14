using MediatR;
using Nut.Results;
using Shuffull.Core.Persistence.Repositories;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Persistence.Specifications.Songs;

namespace Shuffull.Core.Features.Songs.GetSongPage;

public class GetSongPageHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetSongPageQuery, Result<GetSongPageResponse>>
{
    public async Task<Result<GetSongPageResponse>> Handle(GetSongPageQuery request, CancellationToken cancellationToken)
    {
        var songRepository = unitOfWork.Repository<Song>();
        var songsResult = await songRepository.ListAsync(new SongPageSpec(request.PageIndex).AsNoTracking(), cancellationToken);
        if (songsResult.IsError)
        {
            return Result.Error<GetSongPageResponse>(songsResult.GetError().Message);
        }

        var dtos = new List<SongDto>();
        foreach (var song in songsResult.Get())
        {
            var dtoResult = SongDto.Create(song);
            if (dtoResult.IsError)
            {
                return Result.Error<GetSongPageResponse>(dtoResult.GetError().Message);
            }

            dtos.Add(dtoResult.Get());
        }

        // An empty page (past the end of the data) is returned as an empty list rather than an error,
        // unlike the legacy SongController.GetAll which returned 400 for an out-of-range page.
        return Result.Ok(new GetSongPageResponse(request.PageIndex, dtos));
    }
}
