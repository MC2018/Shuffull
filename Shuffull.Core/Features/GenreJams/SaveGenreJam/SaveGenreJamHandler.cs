using MediatR;
using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Persistence;

namespace Shuffull.Core.Features.GenreJams.SaveGenreJam;

/// <summary>
/// Creates or replaces one of the caller's Genre Jams under the id the app chose. The app's outbox sends the whole jam
/// each time, so a resend leaves the same row, and the last save to arrive wins.
///
/// <para>An id that belongs to another user is refused as a <see cref="ErrorKind.Conflict"/> and that row is left
/// alone. So is a create that loses a race with another create of the same id: the app keeps the request and sends it
/// again on its next sync, when it replaces the row the other request made.</para>
/// </summary>
public class SaveGenreJamHandler(ShuffullContext context) : IRequestHandler<SaveGenreJamCommand, Result<SaveGenreJamResponse>>
{
    public async Task<Result<SaveGenreJamResponse>> Handle(SaveGenreJamCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var jam = await context.GenreJams.SingleOrDefaultAsync(j => j.GenreJamId == request.GenreJamId, cancellationToken);
            if (jam is not null && jam.UserId != request.UserId)
            {
                return Result.Error<SaveGenreJamResponse>(
                    ResultErrors.Conflict("This Genre Jam id is already used by another user."));
            }

            if (jam is null)
            {
                jam = new GenreJam { GenreJamId = request.GenreJamId, UserId = request.UserId };
                context.GenreJams.Add(jam);
            }

            jam.Name = request.Name.Trim();
            jam.Whitelists = (request.Whitelists ?? GenreJamFilters.Empty).Normalized();
            jam.Blacklists = (request.Blacklists ?? GenreJamFilters.Empty).Normalized();
            jam.HeavinessMin = request.HeavinessMin;
            jam.HeavinessMax = request.HeavinessMax;
            jam.Version = DateTime.UtcNow;

            await context.SaveChangesAsync(cancellationToken);
            return Result.Ok(new SaveGenreJamResponse(GenreJamDto.Create(jam)));
        }
        catch (Exception ex)
        {
            return Result.Error<SaveGenreJamResponse>(ResultErrors.FromException(ex, $"Could not save the Genre Jam. {ex.Message}"));
        }
    }
}
