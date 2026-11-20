namespace Shuffull.Core.Features.SongDuplicateReports.GetSongDuplicateQueue;

/// <param name="Pairs">The oldest-reported pairs first, at most <see cref="GetSongDuplicateQueueHandler.MaxPairs"/>.</param>
/// <param name="Remaining">How many more pairs are waiting past this page; they show once these are resolved.</param>
public record GetSongDuplicateQueueResponse(IReadOnlyList<SongDuplicateQueuePair> Pairs, int Remaining);

/// <param name="FirstSong">The pair's songs in a fixed order (ordinal by id), so a pair always reads the same way.</param>
/// <param name="FileHashMatch">Both songs carry the same non-empty file hash: the same file uploaded twice.</param>
/// <param name="ReportCount">How many users have an open report on this pair.</param>
public record SongDuplicateQueuePair(
    SongDuplicateQueueSong FirstSong,
    SongDuplicateQueueSong SecondSong,
    bool FileHashMatch,
    int ReportCount,
    IReadOnlyList<SongDuplicateQueueReport> Reports);

/// <param name="DurationSeconds">The longest duration any listen recorded for the song; null when nobody has played
/// it yet. Songs store no duration of their own.</param>
/// <param name="ExternalSongId">The source id (a YouTube video id), null for a manual upload.</param>
/// <param name="LikeCount">How many users have it as Like or Love.</param>
public record SongDuplicateQueueSong(
    string SongId,
    string Name,
    IReadOnlyList<string> Artists,
    double? DurationSeconds,
    string? ExternalSongId,
    int LikeCount);

/// <param name="Kind">"duplicate" or "differentVersion".</param>
/// <param name="PreferredSongId">The copy the reporter wants kept; null means they left it to the curator.</param>
public record SongDuplicateQueueReport(
    string ReportId,
    string ReporterUserId,
    string Kind,
    string? PreferredSongId,
    string? Note,
    DateTime CreatedAt);
