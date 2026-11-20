using MediatR;
using Nut.Results;

namespace Shuffull.Core.Features.SongDuplicateReports.WithdrawSongDuplicateReport;

/// <summary>The reporter takes back report <see cref="ReportId"/> (undo), which lifts their hide.</summary>
public record WithdrawSongDuplicateReportCommand(string UserId, string ReportId)
    : IRequest<Result<WithdrawSongDuplicateReportResponse>>;
