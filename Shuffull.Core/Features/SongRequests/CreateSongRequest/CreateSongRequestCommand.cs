using MediatR;
using Nut.Results;
using Shuffull.Core.Models.Dtos;

namespace Shuffull.Core.Features.SongRequests.CreateSongRequest;

/// <summary>
/// The user asks for a song by its source id, with the search result they picked. See
/// <see cref="CreateSongRequestHandler"/> for what happens to it.
/// </summary>
public record CreateSongRequestCommand(
    string UserId,
    string ExternalSongId,
    string Title,
    IReadOnlyList<string>? Artists,
    int? DurationSeconds,
    string? ThumbnailUrl) : IRequest<Result<SongRequestDto>>;
