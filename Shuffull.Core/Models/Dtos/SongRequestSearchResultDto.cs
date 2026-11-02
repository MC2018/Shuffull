namespace Shuffull.Core.Models.Dtos;

/// <summary>
/// One search result as the app sees it: the producer's <c>SourceSearchResultDto</c> (Shuffull.Metadata) plus two
/// flags Shuffull adds for the caller. <see cref="InLibrary"/>: a song with this <see cref="ExternalSongId"/> is
/// already in the library, so a request completes at once. <see cref="AlreadyRequested"/>: the caller has an open
/// request for it (<c>SongRequestRules.Open</c>, which includes a Failed one the app can retry).
/// </summary>
public record SongRequestSearchResultDto(
    string ExternalSongId,
    string Title,
    List<string> Artists,
    string? Album,
    int? DurationSeconds,
    string? ThumbnailUrl,
    bool IsExplicit,
    bool InLibrary,
    bool AlreadyRequested);
