using MediatR;
using Nut.Results;

namespace Shuffull.Core.Features.Playlists.CreatePlaylist;

public record CreatePlaylistCommand(string UserId, string Name) : IRequest<Result<CreatePlaylistResponse>>;
