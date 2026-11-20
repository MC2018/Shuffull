namespace Shuffull.Core.Models.Enums;

/// <summary>
/// What a user said about a <see cref="Database.SongDuplicateReport"/>. On the wire it is the lower-camel name
/// ("duplicate", "differentVersion").
/// </summary>
public enum SongDuplicateReportKind
{
    /// <summary>The two songs are one recording; a curator may merge them.</summary>
    Duplicate = 0,

    /// <summary>A different version (a cover, a remix, another language) the user doesn't want to hear. Never merged.</summary>
    DifferentVersion = 1,
}
