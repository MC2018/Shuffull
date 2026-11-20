using Shuffull.Core.Models.Dtos;

namespace Shuffull.Core.Features.SongDuplicateReports.GetMySongDuplicateReports;

/// <summary>
/// One page of the caller's reports, every status included (a withdrawn one is how an undo reaches the user's other
/// devices), ordered by Version then ReportId. <see cref="EndOfList"/> is false when more remain: ask again with the
/// last report's exact <c>version</c> and <c>reportId</c>.
/// </summary>
public record GetMySongDuplicateReportsResponse(IReadOnlyList<SongDuplicateReportDto> Reports, bool EndOfList);
