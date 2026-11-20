using Shuffull.Core.Models.Dtos;

namespace Shuffull.Core.Features.SongDuplicateReports.WithdrawSongDuplicateReport;

/// <summary>The report as stored after the request: withdrawn, or merged if a curator merged the pair first.</summary>
public record WithdrawSongDuplicateReportResponse(SongDuplicateReportDto Report);
