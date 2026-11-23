using MediatR;
using Nut.Results;
using Shuffull.Core.Authentication;

namespace Shuffull.Core.Features.SongDuplicateReports.ResolveSongDuplicateReports;

/// <summary>
/// A curator closes every open report on the pair (<see cref="SongId"/>, <see cref="OtherSongId"/>), in either order,
/// without merging: <see cref="Resolution"/> is "notDuplicate" or "keptBoth". Merging is its own command
/// (<c>MergeSongsCommand</c>, Shuffull#142). Gated by <see cref="RequiresRoleAttribute"/>.
/// </summary>
/// <param name="UserId">The curator, recorded as the resolver.</param>
[RequiresRole(Role.Curator)]
public record ResolveSongDuplicateReportsCommand(string UserId, string? SongId, string? OtherSongId, string? Resolution)
    : IRequest<Result<ResolveSongDuplicateReportsResponse>>;
