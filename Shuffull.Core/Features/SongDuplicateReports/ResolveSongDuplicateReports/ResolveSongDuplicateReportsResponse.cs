using Shuffull.Core.Models.Dtos;

namespace Shuffull.Core.Features.SongDuplicateReports.ResolveSongDuplicateReports;

/// <param name="Reports">The reports this request closed. Empty when the pair had none open, which is what a resend
/// sees.</param>
public record ResolveSongDuplicateReportsResponse(IReadOnlyList<SongDuplicateReportDto> Reports);
