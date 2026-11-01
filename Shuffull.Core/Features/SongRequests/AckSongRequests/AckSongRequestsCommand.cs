using MediatR;
using Nut.Results;
using Shuffull.Metadata.Contracts;

namespace Shuffull.Core.Features.SongRequests.AckSongRequests;

/// <summary>Producer: its progress on each request it polled. See <see cref="AckSongRequestsHandler"/>.</summary>
public record AckSongRequestsCommand(IReadOnlyList<SongRequestAckDto> Acks) : IRequest<Result<SongRequestAckResponse>>;
