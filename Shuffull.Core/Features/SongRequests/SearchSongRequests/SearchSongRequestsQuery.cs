using MediatR;
using Nut.Results;
using Shuffull.Core.Models.Dtos;

namespace Shuffull.Core.Features.SongRequests.SearchSongRequests;

/// <summary>Search the external source for songs to request, flagged for the caller. See <see cref="SearchSongRequestsHandler"/>.</summary>
public record SearchSongRequestsQuery(string UserId, string Query, int Limit)
    : IRequest<Result<IReadOnlyList<SongRequestSearchResultDto>>>;
