using MediatR;
using Nut.Results;

namespace Shuffull.Core.Features.UserSongs.CreateUserSongs;

public record CreateUserSongsCommand(string UserId, IReadOnlyList<string> SongIds)
    : IRequest<Result<CreateUserSongsResponse>>;
