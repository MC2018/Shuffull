using Shuffull.Core.Features.SongDuplicateReports;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Models.Dtos;

/// <summary>
/// A duplicate report as the API sends it. <see cref="Kind"/> is "duplicate" or "differentVersion"; <see cref="Status"/>
/// is "pending", "merged", "notDuplicate", "keptBoth" or "withdrawn". The reporter's app hides
/// <see cref="HiddenSongId"/> while the status is pending, notDuplicate or keptBoth (see <see cref="SongDuplicateReport"/>).
/// </summary>
public record SongDuplicateReportDto(
    string ReportId,
    string ReporterUserId,
    string SongId,
    string? OtherSongId,
    string Kind,
    string? PreferredSongId,
    string HiddenSongId,
    string Status,
    string? Note,
    DateTime CreatedAt,
    DateTime? ResolvedAt,
    string? ResolvedByUserId,
    DateTime Version)
{
    public static SongDuplicateReportDto Create(SongDuplicateReport report) => new(
        report.ReportId,
        report.ReporterUserId,
        report.SongId,
        report.OtherSongId,
        SongDuplicateReportWire.ToWire(report.Kind),
        report.PreferredSongId,
        report.HiddenSongId,
        SongDuplicateReportWire.ToWire(report.Status),
        report.Note,
        Utc(report.CreatedAt),
        report.ResolvedAt is { } resolvedAt ? Utc(resolvedAt) : null,
        report.ResolvedByUserId,
        Utc(report.Version));

    // SQLite hands timestamps back with no kind; Postgres as UTC. Either way they were written as UTC.
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
