using MediatR;
using Nut.Results;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Persistence.Repositories;
using Shuffull.Core.Persistence.Specifications.Playlists;

namespace Shuffull.Core.Features.Playlists.GetPlaylistDetails;

public class GetPlaylistDetailsHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetPlaylistDetailsQuery, Result<GetPlaylistDetailsResponse>>
{
    public async Task<Result<GetPlaylistDetailsResponse>> Handle(GetPlaylistDetailsQuery request, CancellationToken cancellationToken)
    {
        var playlistResult = await unitOfWork.Repository<Playlist>()
            .GetAsync(new PlaylistDetailsByUserAndIdSpec(request.UserId, request.PlaylistId).AsNoTracking(), cancellationToken);
        if (playlistResult.IsError)
        {
            return Result.Error<GetPlaylistDetailsResponse>("Playlist not found.");
        }

        var dtoResult = PlaylistDetailsDto.Create(playlistResult.Get());
        if (dtoResult.IsError)
        {
            return Result.Error<GetPlaylistDetailsResponse>(dtoResult.GetError().Message);
        }

        return Result.Ok(new GetPlaylistDetailsResponse(dtoResult.Get()));
    }
}
