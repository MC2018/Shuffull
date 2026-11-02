using MediatR;
using Nut.Results;
using Shuffull.Core.Models.Dtos;

namespace Shuffull.Core.Features.SongRequests.GetMySongRequests;

/// <summary>The user's most recent song requests, newest first, so the app can show their status.</summary>
public record GetMySongRequestsQuery(string UserId) : IRequest<Result<IReadOnlyList<SongRequestDto>>>;
