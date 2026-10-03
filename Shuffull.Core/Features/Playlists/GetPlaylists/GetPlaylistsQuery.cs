using MediatR;
using Nut.Results;

namespace Shuffull.Core.Features.Playlists.GetPlaylists;

public record GetPlaylistsQuery(string UserId, IReadOnlyList<string> PlaylistIds)
    : IRequest<Result<GetPlaylistsResponse>>;
