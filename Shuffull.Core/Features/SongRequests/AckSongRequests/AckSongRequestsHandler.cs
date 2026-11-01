using MediatR;
using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Persistence;
using Shuffull.Metadata.Contracts;

namespace Shuffull.Core.Features.SongRequests.AckSongRequests;

/// <summary>
/// Each ack is one conditional UPDATE that moves the request only from a state its status may follow
/// (<see cref="SongRequestRules.AckMayFollow"/>), so a cancel or an import that commits first is never overwritten;
/// that ack comes back in <see cref="SongRequestAckResponse.Rejected"/>. An ack of the status a request already has is
/// applied again. <c>Unknown</c> or an undefined status is rejected. Of several acks for one request, the last counts.
/// </summary>
public class AckSongRequestsHandler(ShuffullContext context, TimeProvider timeProvider)
    : IRequestHandler<AckSongRequestsCommand, Result<SongRequestAckResponse>>
{
    /// <summary>One poll's worth (<see cref="GetSongRequestQueue.GetSongRequestQueueHandler.Limit"/>).</summary>
    public const int MaxBatch = GetSongRequestQueue.GetSongRequestQueueHandler.Limit;

    public async Task<Result<SongRequestAckResponse>> Handle(AckSongRequestsCommand request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var applied = new List<string>();
        var rejected = new List<string>();
        try
        {
            var acks = request.Acks
                .Where(a => a?.SongRequestId is not null)
                .GroupBy(a => a.SongRequestId)
                .Select(g => g.Last());
            foreach (var ack in acks)
            {
                var from = Enum.IsDefined(ack.Status) ? SongRequestRules.AckMayFollow(ack.Status) : [];
                if (from.Length == 0)
                {
                    rejected.Add(ack.SongRequestId);
                    continue;
                }

                var status = SongRequestRules.ToStatus(ack.Status);
                var reason = status is SongRequestStatus.Retrying or SongRequestStatus.Failed ? Truncate(ack.Reason) : null;
                var updated = await context.SongRequests
                    .Where(r => r.SongRequestId == ack.SongRequestId && from.Contains(r.Status))
                    .ExecuteUpdateAsync(set => set
                        .SetProperty(r => r.Status, status)
                        .SetProperty(r => r.FailureReason, reason)
                        .SetProperty(r => r.UpdatedAt, now), cancellationToken);

                (updated > 0 ? applied : rejected).Add(ack.SongRequestId);
            }
        }
        catch (Exception ex)
        {
            // The acks already applied stay applied; the producer re-sends the batch, and a re-sent ack applies again.
            return Result.Error<SongRequestAckResponse>(ResultErrors.FromException(ex, $"Could not record the song request acks: {ex.Message}"));
        }

        return Result.Ok(new SongRequestAckResponse(applied, rejected));
    }

    private static string? Truncate(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }
        return reason.Length <= SongRequest.MaxFailureReasonLength ? reason : reason[..SongRequest.MaxFailureReasonLength];
    }
}
