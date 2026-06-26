using MediatR;
using Nut.Results;
using Shuffull.Core.Persistence.Repositories;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Persistence.Specifications.Songs;

namespace Shuffull.Core.Features.Songs.GetSongsChanged;

public class GetSongsChangedHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetSongsChangedQuery, Result<GetSongsChangedResponse>>
{
    public async Task<Result<GetSongsChangedResponse>> Handle(GetSongsChangedQuery request, CancellationToken cancellationToken)
    {
        var songsResult = await unitOfWork.Repository<Song>()
            .ListAsync(new SongsByVersionAfterDateSpec(request.AfterDate).AsNoTracking(), cancellationToken);
        if (songsResult.IsError)
        {
            return Result.Error<GetSongsChangedResponse>(songsResult.GetError().Message);
        }

        var songs = songsResult.Get();

        // The spec fetches one extra row; its presence means another page remains. Drop it so the page
        // itself stays at PageSize.
        var endOfList = songs.Count <= SongsByVersionAfterDateSpec.PageSize;
        var page = endOfList ? songs : songs.Take(SongsByVersionAfterDateSpec.PageSize).ToList();

        var dtos = new List<SongDto>();
        foreach (var song in page)
        {
            var dtoResult = SongDto.Create(song);
            if (dtoResult.IsError)
            {
                return Result.Error<GetSongsChangedResponse>(dtoResult.GetError().Message);
            }

            dtos.Add(dtoResult.Get());
        }

        return Result.Ok(new GetSongsChangedResponse(dtos, endOfList));
    }
}
