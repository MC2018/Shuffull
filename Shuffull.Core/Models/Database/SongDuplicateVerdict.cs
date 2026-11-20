using Shuffull.Core.Models.Enums;
using System.ComponentModel.DataAnnotations;

namespace Shuffull.Core.Models.Database;

/// <summary>
/// A curator's latest verdict on an unordered pair of songs, kept apart from the reports it closed. A reporter can
/// still withdraw a resolved report or report the pair again, and either rewrites that report's status; this row is
/// what still says the pair was judged, and how (shuffull-workspace#143).
///
/// <para>Song ids are plain columns, like the reports': a verdict outlives a purge or a merge of either song.</para>
/// </summary>
public class SongDuplicateVerdict
{
    /// <summary>The pair, as <see cref="SongDuplicateReport.PairKeyOf"/> writes it.</summary>
    [Key, MaxLength(SongDuplicateReport.MaxPairKeyLength)]
    public string PairKey { get; set; }

    /// <summary><see cref="SongDuplicateReportStatus.NotDuplicate"/> or <see cref="SongDuplicateReportStatus.KeptBoth"/>.</summary>
    [Required]
    public SongDuplicateReportStatus Verdict { get; set; }

    [Required]
    public DateTime DecidedAt { get; set; }

    /// <summary>The curator. A plain column, so the verdict stays if that account goes.</summary>
    [Required]
    public string DecidedByUserId { get; set; }
}
