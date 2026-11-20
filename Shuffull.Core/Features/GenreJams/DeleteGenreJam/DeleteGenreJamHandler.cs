using MediatR;
using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Persistence;

namespace Shuffull.Core.Features.GenreJams.DeleteGenreJam;

/// <summary>Deletes one of the caller's Genre Jams. Deleting one that isn't there succeeds, so a resend is harmless.</summary>
public class DeleteGenreJamHandler(ShuffullContext context) : IRequestHandler<DeleteGenreJamCommand, Result<DeleteGenreJamResponse>>
{
    public async Task<Result<DeleteGenreJamResponse>> Handle(DeleteGenreJamCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var rows = await context.GenreJams
                .Where(j => j.GenreJamId == request.GenreJamId && j.UserId == request.UserId)
                .ExecuteDeleteAsync(cancellationToken);

            return Result.Ok(new DeleteGenreJamResponse(request.GenreJamId, rows > 0));
        }
        catch (Exception ex)
        {
            return Result.Error<DeleteGenreJamResponse>(ResultErrors.FromException(ex, $"Could not delete the Genre Jam. {ex.Message}"));
        }
    }
}
