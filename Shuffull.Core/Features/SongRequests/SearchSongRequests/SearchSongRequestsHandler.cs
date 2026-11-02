using MediatR;
using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Persistence;
using Shuffull.Core.Services;

namespace Shuffull.Core.Features.SongRequests.SearchSongRequests;

/// <summary>
/// Relays a search to the producer (<see cref="ISourceSearchClient"/>) and flags each result for the caller:
/// <c>InLibrary</c> when a song with its <c>ExternalSongId</c> exists, <c>AlreadyRequested</c> when the caller has an
/// open request for it (<see cref="SongRequestRules.Open"/>, the same rule that refuses a second request). A result
/// with no id or title can't be requested, so it is dropped, and so is a repeated id.
/// </summary>
public class SearchSongRequestsHandler(ShuffullContext context, ISourceSearchClient sourceSearch)
    : IRequestHandler<SearchSongRequestsQuery, Result<IReadOnlyList<SongRequestSearchResultDto>>>
{
    public async Task<Result<IReadOnlyList<SongRequestSearchResultDto>>> Handle(SearchSongRequestsQuery request, CancellationToken cancellationToken)
    {
        var searched = await sourceSearch.SearchAsync(request.Query.Trim(), request.Limit, cancellationToken);
        if (searched.IsError)
        {
            return Result.Error<IReadOnlyList<SongRequestSearchResultDto>>(searched.GetError());
        }

        var results = searched.Get()
            .Where(r => !string.IsNullOrWhiteSpace(r.ExternalSongId) && !string.IsNullOrWhiteSpace(r.Title))
            .DistinctBy(r => r.ExternalSongId)
            .Take(request.Limit)
            .ToList();
        if (results.Count == 0)
        {
            return Result.Ok<IReadOnlyList<SongRequestSearchResultDto>>([]);
        }

        var ids = results.Select(r => r.ExternalSongId).ToList();
        var open = SongRequestRules.Open;
        try
        {
            var inLibrary = (await context.Songs.AsNoTracking()
                .Where(s => s.ExternalSongId != null && ids.Contains(s.ExternalSongId))
                .Select(s => s.ExternalSongId!)
                .ToListAsync(cancellationToken)).ToHashSet();
            var requested = (await context.SongRequests.AsNoTracking()
                .Where(r => r.UserId == request.UserId && ids.Contains(r.ExternalSongId) && open.Contains(r.Status))
                .Select(r => r.ExternalSongId)
                .ToListAsync(cancellationToken)).ToHashSet();

            return Result.Ok<IReadOnlyList<SongRequestSearchResultDto>>(results.Select(r => new SongRequestSearchResultDto(
                r.ExternalSongId, r.Title, r.Artists ?? [], r.Album, r.DurationSeconds, r.ThumbnailUrl, r.IsExplicit,
                inLibrary.Contains(r.ExternalSongId), requested.Contains(r.ExternalSongId))).ToList());
        }
        catch (Exception ex)
        {
            return Result.Error<IReadOnlyList<SongRequestSearchResultDto>>(
                ResultErrors.FromException(ex, $"Could not check the search results against the library: {ex.Message}"));
        }
    }
}
