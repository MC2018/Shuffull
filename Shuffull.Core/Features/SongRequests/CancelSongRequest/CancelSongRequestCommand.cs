using MediatR;
using Nut.Results;
using Shuffull.Core.Models.Dtos;

namespace Shuffull.Core.Features.SongRequests.CancelSongRequest;

/// <summary>
/// The user cancels a request the producer hasn't claimed yet (Pending). Once claimed, the download is under way and
/// the request runs to its end; a Failed one is retried by asking again.
/// </summary>
public record CancelSongRequestCommand(string UserId, string SongRequestId) : IRequest<Result<SongRequestDto>>;
