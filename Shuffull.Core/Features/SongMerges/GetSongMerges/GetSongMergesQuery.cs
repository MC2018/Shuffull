using MediatR;
using Nut.Results;

namespace Shuffull.Core.Features.SongMerges.GetSongMerges;

/// <param name="AfterDate">The <c>version</c> of the last merge the device holds; the start of time for a first sync.</param>
/// <param name="AfterId">That merge's <c>loserSongId</c>, so the next page starts after it even when many merges share
/// one Version. Null pages on Version after AfterDate alone.</param>
public record GetSongMergesQuery(DateTime AfterDate, string? AfterId = null)
    : IRequest<Result<GetSongMergesResponse>>;
