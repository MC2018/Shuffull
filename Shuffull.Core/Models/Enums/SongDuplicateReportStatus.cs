namespace Shuffull.Core.Models.Enums;

/// <summary>
/// Where a <see cref="Database.SongDuplicateReport"/> stands. On the wire it is the lower-camel name ("pending",
/// "notDuplicate", ...).
/// </summary>
public enum SongDuplicateReportStatus
{
    /// <summary>Waiting for a curator, or, with no other song, a personal hide that no curator needs to see.</summary>
    Pending = 0,

    /// <summary>A curator merged the pair (Shuffull#142).</summary>
    Merged = 1,

    /// <summary>A curator found the two songs are not the same.</summary>
    NotDuplicate = 2,

    /// <summary>A curator found them different versions, and kept both.</summary>
    KeptBoth = 3,

    /// <summary>The reporter took the report back (undo). The row stays, so the undo reaches every device.</summary>
    Withdrawn = 4,
}
