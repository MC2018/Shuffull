using MediatR;
using Nut.Results;

namespace Shuffull.Core.Features.Playlists.GetPlaylistDetails;

public record GetPlaylistDetailsQuery(string UserId, string PlaylistId)
    : IRequest<Result<GetPlaylistDetailsResponse>>;
