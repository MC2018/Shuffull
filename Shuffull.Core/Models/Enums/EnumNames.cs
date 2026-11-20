namespace Shuffull.Core.Models.Enums;

/// <summary>Enum values as names on the wire, for the endpoints that send and read enums as strings.</summary>
public static class EnumNames
{
    /// <summary>
    /// Matches an enum member by name only, ignoring case. Unlike <c>Enum.TryParse</c> it refuses numbers, so "7"
    /// is rejected rather than stored as a value no member has.
    /// </summary>
    public static T? Parse<T>(string? value) where T : struct, Enum
        => Enum.GetValues<T>().Cast<T?>().FirstOrDefault(v => string.Equals(v.ToString(), value, StringComparison.OrdinalIgnoreCase));
}
