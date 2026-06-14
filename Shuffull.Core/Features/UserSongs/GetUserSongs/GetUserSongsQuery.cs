using MediatR;
using Nut.Results;

namespace Shuffull.Core.Features.UserSongs.GetUserSongs;

public record GetUserSongsQuery(string UserId, DateTime AfterDate)
    : IRequest<Result<GetUserSongsResponse>>;
