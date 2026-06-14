using MediatR;
using Nut.Results;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Persistence.Repositories;
using Shuffull.Core.Persistence.Specifications.Playlists;

namespace Shuffull.Core.Features.Playlists.GetUserPlaylists;

public class GetUserPlaylistsHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetUserPlaylistsQuery, Result<GetUserPlaylistsResponse>>
{
    public async Task<Result<GetUserPlaylistsResponse>> Handle(GetUserPlaylistsQuery request, CancellationToken cancellationToken)
    {
        var playlistRepository = unitOfWork.Repository<Playlist>();
        var playlistsResult = await playlistRepository.ListAsync(new PlaylistsByUserSpec(request.UserId).AsNoTracking(), cancellationToken);
        if (playlistsResult.IsError)
        {
            return Result.Error<GetUserPlaylistsResponse>(playlistsResult.GetError().Message);
        }

        var dtos = new List<PlaylistDto>();
        foreach (var playlist in playlistsResult.Get())
        {
            var dtoResult = PlaylistDto.Create(playlist);
            if (dtoResult.IsError)
            {
                return Result.Error<GetUserPlaylistsResponse>(dtoResult.GetError().Message);
            }

            dtos.Add(dtoResult.Get());
        }

        return Result.Ok(new GetUserPlaylistsResponse(dtos));
    }
}
