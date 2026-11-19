using System.Text.Json;

namespace Shuffull.Core.Models.Dtos;

/// <summary>
/// One side (include or exclude) of a Genre Jam's filters, in the shape the app keeps it (its <c>WhitelistSetting</c>).
/// Every id means the same thing on every device: tag and playlist ids come from the server, and the app derives an
/// artist id from the artist's name. A list the app leaves out reads as empty.
/// </summary>
public record GenreJamFilters(
    IReadOnlyList<string>? ArtistIds = null,
    IReadOnlyList<string>? PlaylistIds = null,
    IReadOnlyList<string>? GenreIds = null,
    IReadOnlyList<string>? TimePeriodIds = null,
    IReadOnlyList<string>? LanguageIds = null,
    IReadOnlyList<string>? MoodIds = null,
    IReadOnlyList<string>? ThemeIds = null)
{
    public static readonly GenreJamFilters Empty = new GenreJamFilters().Normalized();

    // camelCase like the wire, so a stored jam reads the same in the database as in the API.
    private static readonly JsonSerializerOptions StorageOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Every list, null ones as empty, for checking and storing.</summary>
    public IEnumerable<IReadOnlyList<string>> AllLists() =>
        new[] { ArtistIds, PlaylistIds, GenreIds, TimePeriodIds, LanguageIds, MoodIds, ThemeIds }.Select(x => x ?? []);

    /// <summary>The same filters with no null lists, which is how they are stored and sent.</summary>
    public GenreJamFilters Normalized() => new(
        ArtistIds ?? [], PlaylistIds ?? [], GenreIds ?? [], TimePeriodIds ?? [], LanguageIds ?? [], MoodIds ?? [], ThemeIds ?? []);

    /// <summary>The database column's text. Used by the value converter in <c>ShuffullContext</c>.</summary>
    public static string ToJson(GenreJamFilters filters) => JsonSerializer.Serialize(filters.Normalized(), StorageOptions);

    public static GenreJamFilters FromJson(string json) =>
        (JsonSerializer.Deserialize<GenreJamFilters>(json, StorageOptions) ?? Empty).Normalized();
}
