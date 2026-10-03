using MediatR;
using Nut.Results;
using Shuffull.Core.Models.Enums;

namespace Shuffull.Core.Features.UserSongs.SetSongLikeStatus;

/// <summary>Sets the authenticated user's sentiment (<see cref="LikeStatus"/>) toward one of their songs.</summary>
public record SetSongLikeStatusCommand(string UserId, string SongId, LikeStatus LikeStatus)
    : IRequest<Result<SetSongLikeStatusResponse>>;
