using MediatR;
using Nut.Results;

namespace Shuffull.Core.Features.Playlists.GetUserPlaylists;

public record GetUserPlaylistsQuery(string UserId) : IRequest<Result<GetUserPlaylistsResponse>>;
