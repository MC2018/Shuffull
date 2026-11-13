using MediatR;
using Nut.Results;
using Shuffull.Core.Models.Dtos;

namespace Shuffull.Core.Features.Playback.GetPlaybackSessions;

/// <summary>
/// The caller's devices for the "play on" picker (Shuffull#6): each device active in the last 30 days, most recently
/// active first, with whether it is connected to the playback hub and its session if it has one. Scoped to
/// <paramref name="UserId"/>.
/// </summary>
public record GetPlaybackSessionsQuery(string UserId) : IRequest<Result<GetPlaybackSessionsResponse>>;
