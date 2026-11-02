using MediatR;
using Nut.Results;
using Shuffull.Core.Models.Enums;
using Shuffull.Metadata.Contracts;

namespace Shuffull.Core.Features.SongRequests.GetSongRequestQueue;

/// <summary>Producer: the requests at <paramref name="Status"/> (the funnel asks for Pending), oldest first.</summary>
public record GetSongRequestQueueQuery(SongRequestStatus Status) : IRequest<Result<IReadOnlyList<PendingSongRequestDto>>>;
