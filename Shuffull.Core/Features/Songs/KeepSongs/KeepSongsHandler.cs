using MediatR;
using Nut.Results;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence.Repositories;
using Shuffull.Core.Persistence.Specifications.UserSongs;
using Shuffull.Core.Services;

namespace Shuffull.Core.Features.Songs.KeepSongs;

/// <summary>
/// Promotes the caller's kept songs out of audition via <see cref="IAuditionPromotionService"/> — the same durable
/// write RetagSongs makes before enriching, without the enrichment or the curator gate. Ownership-scoped: only
/// songs the user has a <c>UserSong</c> for are promoted.
/// </summary>
public class KeepSongsHandler(IUnitOfWork unitOfWork, IAuditionPromotionService promotion)
    : IRequestHandler<KeepSongsCommand, Result<KeepSongsResponse>>
{
    // Matches RetagSongsHandler.MaxBatch, which is what the app's outbox chunks to.
    public const int MaxBatch = 200;

    public async Task<Result<KeepSongsResponse>> Handle(KeepSongsCommand request, CancellationToken cancellationToken)
    {
        var order = (request.SongIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .ToList();

        if (order.Count == 0)
        {
            return Result.Ok(new KeepSongsResponse([]));
        }
        if (order.Count > MaxBatch)
        {
            return Result.Error<KeepSongsResponse>($"Too many songs in one request ({order.Count}); max {MaxBatch}.");
        }

        var userSongsResult = await unitOfWork.Repository<UserSong>()
            .ListAsync(new UserSongsByUserAndSongIdsSpec(request.UserId, order), cancellationToken);
        if (userSongsResult.IsError)
        {
            return Result.Error<KeepSongsResponse>(userSongsResult.GetError().Message);
        }

        var owned = userSongsResult.Get().Select(us => us.SongId).ToHashSet();
        var toPromote = order.Where(owned.Contains).ToList();

        // A failure aborts the whole batch rather than reporting per-item failures: the caller (the app's
        // outbox) then keeps its rows and retries, instead of reading "failed" as a verdict on the song.
        var promoteResult = await promotion.PromoteAsync(toPromote, cancellationToken);
        if (promoteResult.IsError)
        {
            return Result.Error<KeepSongsResponse>(
                $"Could not record the keep for this batch; no songs were kept. {promoteResult.GetError().Message}");
        }

        var results = order
            .Select(songId => owned.Contains(songId)
                ? new SongKeepResult(songId, KeepOutcomes.Kept)
                : new SongKeepResult(songId, KeepOutcomes.Failed, "Song is not in your library."))
            .ToList();

        return Result.Ok(new KeepSongsResponse(results));
    }
}
