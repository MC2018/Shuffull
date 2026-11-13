namespace Shuffull.Core.Models.Dtos;

/// <summary>One flagged song as the funnel polls it, with the source id snapshotted when it was flagged.</summary>
public record SongReplacementDto(
    string SongReplacementId,
    string SongId,
    string Name,
    List<string> Artists,
    string? OriginalExternalSongId,
    string? Note,
    DateTime CreatedAt);
