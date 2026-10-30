using System.Globalization;
using System.Text.RegularExpressions;

namespace Shuffull.Core.Features.Listens.GetRecap;

/// <summary>
/// The zone a recap is computed in: an IANA id ("Europe/Berlin"), a fixed offset ("+02:00", "-0530") or "UTC".
/// Months, days, hours and the year itself are the user's local ones, so a listen at 23:30 on 31 December in New York
/// belongs to that December, not to January in UTC.
/// </summary>
public static partial class RecapTimeZone
{
    /// <summary>Recaps asked for without a zone are computed in UTC.</summary>
    public const string Default = "UTC";

    // Offsets on Earth run from -12:00 to +14:00.
    private static readonly TimeSpan MaxOffset = TimeSpan.FromHours(14);

    // The id goes to the OS time zone database, so only the characters IANA ids use, and no "..".
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_+\-]*(/[A-Za-z0-9_+\-]+)*$")]
    private static partial Regex IanaIdPattern();

    [GeneratedRegex(@"^(?<sign>[+-])(?<h>\d{2}):?(?<m>\d{2})$")]
    private static partial Regex OffsetPattern();

    /// <summary>Resolves <paramref name="value"/>; null or blank is UTC. False for anything unknown or malformed.</summary>
    public static bool TryParse(string? value, out TimeZoneInfo zone)
    {
        zone = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(value) || value is "UTC" or "Z")
        {
            return true;
        }
        if (value.Length > 64)
        {
            return false;
        }

        var offset = OffsetPattern().Match(value);
        if (offset.Success)
        {
            var span = new TimeSpan(int.Parse(offset.Groups["h"].Value, CultureInfo.InvariantCulture),
                int.Parse(offset.Groups["m"].Value, CultureInfo.InvariantCulture), 0);
            if (offset.Groups["m"].Value is not ("00" or "15" or "30" or "45") || span > MaxOffset)
            {
                return false;
            }
            span = offset.Groups["sign"].Value == "-" ? span.Negate() : span;
            var id = (span < TimeSpan.Zero ? "-" : "+") + span.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
            zone = TimeZoneInfo.CreateCustomTimeZone(id, span, id, id);
            return true;
        }

        if (!IanaIdPattern().IsMatch(value) || value.Contains(".."))
        {
            return false;
        }
        if (TimeZoneInfo.TryFindSystemTimeZoneById(value, out var found))
        {
            zone = found;
            return true;
        }
        return false;
    }

    /// <summary>
    /// The UTC instant local midnight starts on 1 January of <paramref name="year"/> in <paramref name="zone"/>. A zone
    /// that skipped that midnight (a DST jump at 00:00) starts the year at the first local time that exists.
    /// </summary>
    public static DateTime StartOfYearUtc(int year, TimeZoneInfo zone)
    {
        var local = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(15);
        }
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }
}
