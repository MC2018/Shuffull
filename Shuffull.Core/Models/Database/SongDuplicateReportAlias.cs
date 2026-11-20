using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Shuffull.Core.Models.Database;

/// <summary>
/// A report id the app sent for a pair its user had already reported under another id. The server folds the second
/// report into the first and keeps the new id here, so a withdraw or resend the outbox queued under it still reaches
/// the report it became.
/// </summary>
[Index(nameof(ReportId))]
public class SongDuplicateReportAlias
{
    [Key, MaxLength(SongDuplicateReport.MaxIdLength)]
    public string AliasId { get; set; }

    /// <summary>The report it was folded into.</summary>
    [Required, MaxLength(SongDuplicateReport.MaxIdLength)]
    public string ReportId { get; set; }

    [ForeignKey(nameof(ReportId))]
    public SongDuplicateReport Report { get; set; }
}
