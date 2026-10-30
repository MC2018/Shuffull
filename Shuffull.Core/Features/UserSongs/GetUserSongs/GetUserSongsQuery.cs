using MediatR;
using Nut.Results;

namespace Shuffull.Core.Features.UserSongs.GetUserSongs;

/// <param name="AfterId">SongId of the last record the client holds at <paramref name="AfterDate"/>; null = the
/// legacy <c>Version &gt; AfterDate</c> cursor that installed apps send.</param>
public record GetUserSongsQuery(string UserId, DateTime AfterDate, string? AfterId = null)
    : IRequest<Result<GetUserSongsResponse>>;
