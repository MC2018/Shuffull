using Shuffull.Core.Models.Enums;

namespace Shuffull.Core.Models.Dtos;

/// <summary>
/// One of the user's song requests, as the app sees it. <see cref="Status"/> is a <see cref="SongRequestStatus"/> int;
/// <see cref="SongId"/> is set once it is <see cref="SongRequestStatus.Completed"/>.
/// </summary>
public record SongRequestDto(
    string SongRequestId,
    string ExternalSongId,
    string Title,
    List<string> Artists,
    int? DurationSeconds,
    string? ThumbnailUrl,
    SongRequestStatus Status,
    string? FailureReason,
    string? SongId,
    string TargetPlaylistName,
    DateTime CreatedAt,
    DateTime UpdatedAt);
