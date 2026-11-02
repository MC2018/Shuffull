namespace Shuffull.Core.Models.Dtos;

/// <summary>
/// One search result as the app sees it: the producer's <c>SourceSearchResultDto</c> (Shuffull.Metadata) plus what
/// Shuffull adds for the caller. <see cref="InLibrary"/>: a song with this <see cref="ExternalSongId"/> is already in
/// the library, so a request completes at once. <see cref="AlreadyRequested"/>: the caller has an open request for it
/// (<c>SongRequestRules.Open</c>, which includes a Failed one the app can retry). <see cref="SongId"/>: that library
/// song, set exactly when <see cref="InLibrary"/> is. <see cref="InUserLibrary"/>: the caller already has it, so the
/// app plays <see cref="SongId"/> instead of requesting it, which would add it to this week's requests playlist again
/// (Shuffull#117).
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
    bool AlreadyRequested,
    string? SongId,
    bool InUserLibrary);
