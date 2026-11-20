using MediatR;
using Nut.Results;

namespace Shuffull.Core.Features.SongDuplicateReports.GetMySongDuplicateReports;

/// <param name="AfterDate">The <c>version</c> of the last report the device holds; the start of time for a first sync.</param>
/// <param name="AfterId">That report's <c>reportId</c>, so the next page starts after it even when many reports share
/// one Version. Null pages on Version after AfterDate alone.</param>
public record GetMySongDuplicateReportsQuery(string UserId, DateTime AfterDate, string? AfterId = null)
    : IRequest<Result<GetMySongDuplicateReportsResponse>>;
