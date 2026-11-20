using MediatR;
using Nut.Results;
using Shuffull.Core.Authentication;

namespace Shuffull.Core.Features.SongDuplicateReports.GetSongDuplicateQueue;

/// <summary>
/// The curator's queue of open duplicate reports, one entry per unordered pair of songs. Gated by
/// <see cref="RequiresRoleAttribute"/>, so a non-curator is rejected before the handler runs.
/// </summary>
[RequiresRole(Role.Curator)]
public record GetSongDuplicateQueueQuery : IRequest<Result<GetSongDuplicateQueueResponse>>;
