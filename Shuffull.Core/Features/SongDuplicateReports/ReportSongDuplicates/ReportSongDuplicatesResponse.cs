using Shuffull.Core.Models.Dtos;

namespace Shuffull.Core.Features.SongDuplicateReports.ReportSongDuplicates;

/// <summary>
/// Per-report outcome, the same shape as the Keep and listen results so the app's outbox reads them the same way.
/// <see cref="ReportId"/> is the id the app sent. <see cref="Report"/> is the stored row after this request, null when
/// rejected. After <see cref="SongDuplicateReportOutcomes.Updated"/> its <c>reportId</c> is the earlier report's, which
/// is the id to withdraw it by.
/// </summary>
public record SongDuplicateReportResult(string? ReportId, string Outcome, string? Error = null, SongDuplicateReportDto? Report = null);

/// <summary>The stable outcome strings on the wire.</summary>
public static class SongDuplicateReportOutcomes
{
    /// <summary>Stored under this id: a new report, or a resend of one already stored.</summary>
    public const string Recorded = "recorded";

    /// <summary>The caller had already reported this pair under another id; that report now says this one.</summary>
    public const string Updated = "updated";

    /// <summary>Not stored, and a resend won't change that: an unknown song, one not in the caller's library, or an invalid item.</summary>
    public const string Rejected = "rejected";
}

/// <summary>One result per item, in request order.</summary>
public record ReportSongDuplicatesResponse(IReadOnlyList<SongDuplicateReportResult> Results);
