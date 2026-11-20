using Shuffull.Core.Features.SongDuplicateReports.ReportSongDuplicates;

namespace Shuffull.Api.Commands.SongDuplicateReports.ReportSongDuplicates;

/// <summary>Body for the batched duplicate report: one item per report, each with an id the app generated.</summary>
public record ReportSongDuplicatesRequest(SongDuplicateReportItem[]? Reports);
