namespace Shuffull.Api.Commands.SongDuplicateReports.ResolveSongDuplicateReports;

/// <summary>Body for a curator's verdict on a pair: the two songs, in either order, and "notDuplicate" or "keptBoth".</summary>
public record ResolveSongDuplicateReportsRequest(string? SongId, string? OtherSongId, string? Resolution);
