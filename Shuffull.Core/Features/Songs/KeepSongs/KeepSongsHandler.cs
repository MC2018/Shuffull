using MediatR;
using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence.Repositories;
using Shuffull.Core.Persistence.Specifications.UserSongs;
using Shuffull.Core.Services;
using Shuffull.Core.Alerts;
using Shuffull.Core.Services.Alerts;

namespace Shuffull.Core.Features.Songs.KeepSongs;

/// <summary>
/// Promotes the caller's kept songs out of audition via <see cref="IAuditionPromotionService"/> — the same durable
/// write RetagSongs makes before enriching, without the enrichment or the curator gate. Ownership-scoped: only
/// songs the user has a <c>UserSong</c> for are promoted.
///
/// <para>A refusal is also recorded as a <c>keep-refused:</c> health signal, which emails the owner (Shuffull#75):
/// the app holds the playlist's delete while that Keep is outstanding, and shows nothing.</para>
/// </summary>
public class KeepSongsHandler(IUnitOfWork unitOfWork, IAuditionPromotionService promotion, IHealthSignals signals)
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
            return Result.Error<KeepSongsResponse>(userSongsResult.GetError());
        }

        var owned = userSongsResult.Get().Select(us => us.SongId).ToHashSet();
        var toPromote = order.Where(owned.Contains).ToList();

        // A failure aborts the whole batch rather than reporting per-item failures: the caller (the app's
        // outbox) then keeps its rows and retries, instead of reading "failed" as a verdict on the song.
        var promoteResult = await promotion.PromoteAsync(toPromote, cancellationToken);
        if (promoteResult.IsError)
        {
            return Result.Error<KeepSongsResponse>(promoteResult.GetError().WithMessage(
                $"Could not record the keep for this batch; no songs were kept. {promoteResult.GetError().Message}"));
        }

        var results = order
            .Select(songId => owned.Contains(songId)
                ? new SongKeepResult(songId, KeepOutcomes.Kept)
                : new SongKeepResult(songId, KeepOutcomes.Failed, "Song is not in your library."))
            .ToList();

        // One signal per refused song (see ShuffullSignalRules.KeepRefusedKey), each its own write in its own scope,
        // after the Keep is recorded. A failure to record is logged, never returned.
        foreach (var refusal in results.Where(r => r.Outcome == KeepOutcomes.Failed))
        {
            await signals.FailureAsync(ShuffullSignalRules.KeepRefusedKey(request.UserId, refusal.SongId),
                $"User {request.UserId}, song {refusal.SongId}: {refusal.Error}", cancellationToken);
        }

        return Result.Ok(new KeepSongsResponse(results));
    }
}
