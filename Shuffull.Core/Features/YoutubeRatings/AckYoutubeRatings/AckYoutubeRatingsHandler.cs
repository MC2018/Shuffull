using MediatR;
using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Persistence;

namespace Shuffull.Core.Features.YoutubeRatings.AckYoutubeRatings;

/// <summary>
/// Each ack echoes the rating the producer actually pushed, and a row is only resolved if it is still Pending with that
/// exact rating. If the user re-rated during the producer's poll→apply window (the row is now Pending with a newer
/// value), it is left for the next cycle rather than being silently resolved with the stale value. Of several acks for
/// one row, the last counts.
///
/// <para><see cref="YoutubeRatingAck.Outcome"/> decides which terminal state: <c>applied</c> (or null, for older
/// producers) completes the row; <c>unratable</c> marks it Skipped because YouTube will never accept it — the owner has
/// disabled ratings. ONLY a positively-identified permanent refusal may be sent as unratable. A transient failure (out
/// of quota, network, expired token) must not be acked at all, so the row stays Pending and is retried after the daily
/// reset.</para>
///
/// <para>No catch: a database fault stays an unhandled 500, as it was before this moved out of the controller. The
/// acks already applied stay applied, and a re-sent ack of a resolved row counts nothing.</para>
/// </summary>
public class AckYoutubeRatingsHandler(ShuffullContext context, TimeProvider timeProvider)
    : IRequestHandler<AckYoutubeRatingsCommand, Result<AckYoutubeRatingsResponse>>
{
    public async Task<Result<AckYoutubeRatingsResponse>> Handle(AckYoutubeRatingsCommand request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var completed = 0;
        var skipped = 0;
        foreach (var ack in request.Acks.GroupBy(a => a.RatingRequestId).Select(g => g.Last()))
        {
            // Unknown outcome strings deliberately fall through to Completed rather than erroring: an older
            // producer sends no outcome at all, and treating that as "applied" preserves the original contract.
            // Only the explicit unratable marker is terminal-without-success.
            var unratable = string.Equals(ack.Outcome, YoutubeRatingOutcomes.Unratable, StringComparison.OrdinalIgnoreCase);
            var status = unratable ? YoutubeRatingStatus.Skipped : YoutubeRatingStatus.Completed;
            var applied = YoutubeRatingApiValues.FromApi(ack.Rating);

            // One conditional UPDATE, so the Pending-with-this-rating check and the write are a single statement.
            // Checking a loaded row and saving it later let a re-rate that committed in between be marked done
            // with a rating YouTube never got (Shuffull#43).
            var updated = await context.YoutubeRatingRequests
                .Where(r => r.YoutubeRatingRequestId == ack.RatingRequestId
                            && r.Status == YoutubeRatingStatus.Pending
                            && r.Rating == applied)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(r => r.Status, status)
                    .SetProperty(r => r.UpdatedAt, now), cancellationToken);

            if (unratable)
            {
                skipped += updated;
            }
            else
            {
                completed += updated;
            }
        }

        return Result.Ok(new AckYoutubeRatingsResponse(completed, skipped));
    }
}
