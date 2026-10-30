using MediatR;
using Nut.Results;

namespace Shuffull.Core.Features.Listens.RecordListens;

/// <summary>
/// One listen as the app sends it. Every field is nullable so that a missing or malformed one is reported against
/// that item (<see cref="ListenOutcomes.Rejected"/>) instead of failing the whole batch. <see cref="EndReason"/> and
/// <see cref="Context"/> are the enum names, matched without case ("skipped", "genreJam").
/// </summary>
/// <param name="UserId">Optional. When sent it must be the caller's own id.</param>
public record ListenItem(
    string? ListenId,
    string? SongId,
    DateTime? StartedAt,
    DateTime? EndedAt,
    double? SecondsListened,
    double? SongDurationSeconds,
    string? EndReason,
    string? Context,
    string? PlaylistId = null,
    string? Platform = null,
    string? UserId = null);

/// <summary>
/// Appends the caller's listens to their listen log. A listen is history: it is recorded whether or not the user
/// still has the song, it never touches <c>UserSong</c>, and it runs no AI. Each item gets its own outcome.
/// </summary>
public record RecordListensCommand(string UserId, IReadOnlyList<ListenItem> Listens) : IRequest<Result<RecordListensResponse>>;
