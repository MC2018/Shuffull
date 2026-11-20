using MediatR;
using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Persistence;

namespace Shuffull.Core.Features.GenreJams.GetGenreJams;

/// <summary>Every Genre Jam the caller has saved, by name. The app replaces its own list with this on each sync.</summary>
public class GetGenreJamsHandler(ShuffullContext context) : IRequestHandler<GetGenreJamsQuery, Result<GetGenreJamsResponse>>
{
    public async Task<Result<GetGenreJamsResponse>> Handle(GetGenreJamsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var jams = await context.GenreJams
                .AsNoTracking()
                .Where(j => j.UserId == request.UserId)
                .OrderBy(j => j.Name)
                .ThenBy(j => j.GenreJamId)
                .ToListAsync(cancellationToken);

            return Result.Ok(new GetGenreJamsResponse(jams.Select(GenreJamDto.Create).ToList()));
        }
        catch (Exception ex)
        {
            return Result.Error<GetGenreJamsResponse>(ResultErrors.FromException(ex, $"Could not load Genre Jams. {ex.Message}"));
        }
    }
}
