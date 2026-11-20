using Shuffull.Core.Models.Enums;

namespace Shuffull.Core.Features.SongDuplicateReports;

/// <summary>
/// The kind and status of a duplicate report as strings on the wire: the lower-camel enum name ("differentVersion",
/// "notDuplicate"), as listens do (enums would otherwise serialize as ints). Read back with
/// <see cref="EnumNames.Parse{T}"/>.
/// </summary>
public static class SongDuplicateReportWire
{
    public static string ToWire<T>(T value) where T : struct, Enum
    {
        var name = value.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    /// <summary>The statuses a curator can resolve a pair to here. Merging is Shuffull#142.</summary>
    public static readonly SongDuplicateReportStatus[] Resolutions =
        [SongDuplicateReportStatus.NotDuplicate, SongDuplicateReportStatus.KeptBoth];
}
