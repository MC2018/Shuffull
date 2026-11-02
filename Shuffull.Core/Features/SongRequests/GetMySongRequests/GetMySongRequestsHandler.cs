using MediatR;
using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Persistence;

namespace Shuffull.Core.Features.SongRequests.GetMySongRequests;

public class GetMySongRequestsHandler(ShuffullContext context)
    : IRequestHandler<GetMySongRequestsQuery, Result<IReadOnlyList<SongRequestDto>>>
{
    /// <summary>How many of the user's requests the list returns.</summary>
    public const int Limit = 50;

    public async Task<Result<IReadOnlyList<SongRequestDto>>> Handle(GetMySongRequestsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var rows = await context.SongRequests
                .AsNoTracking()
                .Where(r => r.UserId == request.UserId)
                .OrderByDescending(r => r.CreatedAt)
                .ThenByDescending(r => r.SongRequestId)
                .Take(Limit)
                .ToListAsync(cancellationToken);
            return Result.Ok<IReadOnlyList<SongRequestDto>>(rows.Select(SongRequestMapping.ToDto).ToList());
        }
        catch (Exception ex)
        {
            return Result.Error<IReadOnlyList<SongRequestDto>>(ResultErrors.FromException(ex, $"Could not read your song requests: {ex.Message}"));
        }
    }
}
