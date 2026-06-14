using MediatR;
using Nut.Results;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Persistence.Repositories;
using Shuffull.Core.Persistence.Specifications.Playlists;

namespace Shuffull.Core.Features.Playlists.GetPlaylists;

public class GetPlaylistsHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetPlaylistsQuery, Result<GetPlaylistsResponse>>
{
    public async Task<Result<GetPlaylistsResponse>> Handle(GetPlaylistsQuery request, CancellationToken cancellationToken)
    {
        var playlistsResult = await unitOfWork.Repository<Playlist>()
            .ListAsync(new PlaylistsByUserAndIdsSpec(request.UserId, request.PlaylistIds.ToList()).AsNoTracking(), cancellationToken);
        if (playlistsResult.IsError)
        {
            return Result.Error<GetPlaylistsResponse>(playlistsResult.GetError().Message);
        }

        var dtos = new List<PlaylistDto>();
        foreach (var playlist in playlistsResult.Get())
        {
            var dtoResult = PlaylistDto.Create(playlist);
            if (dtoResult.IsError)
            {
                return Result.Error<GetPlaylistsResponse>(dtoResult.GetError().Message);
            }

            dtos.Add(dtoResult.Get());
        }

        return Result.Ok(new GetPlaylistsResponse(dtos));
    }
}
