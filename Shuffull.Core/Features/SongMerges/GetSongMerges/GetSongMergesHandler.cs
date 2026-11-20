using MediatR;
using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Persistence;

namespace Shuffull.Core.Features.SongMerges.GetSongMerges;

/// <summary>
/// Every merge after the device's cursor (Shuffull#142), for any signed-in user: songs are shared rows, so every
/// device needs every merge to swap a loser's id for its survivor's. Pages on the keyset (Version, LoserSongId),
/// which steps through a tie of any size.
/// </summary>
public class GetSongMergesHandler(ShuffullContext context)
    : IRequestHandler<GetSongMergesQuery, Result<GetSongMergesResponse>>
{
    public const int PageSize = 500;

    public async Task<Result<GetSongMergesResponse>> Handle(GetSongMergesQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var afterDate = request.AfterDate.Kind switch
            {
                DateTimeKind.Utc => request.AfterDate,
                DateTimeKind.Unspecified => DateTime.SpecifyKind(request.AfterDate, DateTimeKind.Utc),
                _ => request.AfterDate.ToUniversalTime(),
            };
            var afterId = string.IsNullOrEmpty(request.AfterId) ? null : request.AfterId;

            var merges = context.SongMerges.AsNoTracking();
            var rows = await (afterId is null
                    ? merges.Where(m => m.Version > afterDate)
                    : merges.Where(m => m.Version > afterDate || (m.Version == afterDate && string.Compare(m.LoserSongId, afterId) > 0)))
                .OrderBy(m => m.Version)
                .ThenBy(m => m.LoserSongId)
                .Take(PageSize + 1)
                .ToListAsync(cancellationToken);

            // One row past the page means another page remains.
            var endOfList = rows.Count <= PageSize;
            return Result.Ok(new GetSongMergesResponse(rows.Take(PageSize).Select(SongMergeDto.Create).ToList(), endOfList));
        }
        catch (Exception ex)
        {
            return Result.Error<GetSongMergesResponse>(ResultErrors.FromException(ex, $"Could not load song merges. {ex.Message}"));
        }
    }
}
