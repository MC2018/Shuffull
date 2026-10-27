using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Persistence;

namespace Shuffull.Core.Services.Alerts.Checks;

/// <summary>
/// Likes waiting more than a day to reach YouTube. The funnel polls every 10 minutes and a failed apply stays
/// Pending, so a day-old Pending row means the like-sync is broken. 177 rows sat that way from 2026-07-20 to
/// 08-22 with nobody noticing (the youtube worker wasn't on shuffull-ingest-net).
///
/// <para>Ages are read from <c>UpdatedAt</c>, not <c>CreatedAt</c>: a re-rated video reopens its old row as
/// Pending, keeping a CreatedAt from whenever it was first rated. UpdatedAt is when it last became (or changed
/// while) Pending, which is how long this rating has waited.</para>
/// </summary>
public sealed class LikeSyncBacklogCheck(ShuffullContext db) : IAlertCheck
{
    public static readonly TimeSpan PendingTooLong = TimeSpan.FromHours(24);

    public string KeyPrefix => "like-sync";

    public async Task<Result<IReadOnlyList<AlertFinding>>> EvaluateAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var before = nowUtc - PendingTooLong;
        var query = db.YoutubeRatingRequests.AsNoTracking()
            .Where(r => r.Status == YoutubeRatingStatus.Pending && r.UpdatedAt < before);
        var count = await query.CountAsync(cancellationToken);
        if (count == 0)
        {
            return Result.Ok<IReadOnlyList<AlertFinding>>([]);
        }

        var oldest = await query.MinAsync(r => r.UpdatedAt, cancellationToken);

        return Result.Ok<IReadOnlyList<AlertFinding>>(
        [
            new AlertFinding("like-sync:backlog", 2, $"{count} like(s) haven't reached YouTube in a day",
                $"{count} YouTube rating request(s) have been Pending for over {PendingTooLong.TotalHours:0} h; the " +
                $"oldest since {oldest:yyyy-MM-dd HH:mm} UTC. The funnel's youtube worker either isn't polling " +
                "(check the youtube-ratings poll alert), can't reach YouTube (quota or an expired sign-in), or can't " +
                "ack. Nothing is lost: they stay Pending and apply once it works.",
                ShuffullSignalRules.Daily),
        ]);
    }
}
