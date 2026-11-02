using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Persistence;
using Shuffull.Core.Services.Alerts;

namespace Shuffull.Core.Alerts.Checks;

/// <summary>
/// Song requests (Shuffull#57) left waiting for the producer (Shuffull#112). The funnel polls
/// <c>GET song-requests?status=Pending</c> every few seconds and claims each request with an Accepted ack, so a
/// request should leave Pending almost at once. Two ways it doesn't, each its own alert:
/// <list type="bullet">
/// <item><b>not polled</b>: requests are Pending and the producer hasn't polled for <see cref="PollSilentAfter"/>
/// (it is down, or can't reach the API: the like-sync once sat a month that way, off shuffull-ingest-net);</item>
/// <item><b>stuck</b>: the producer polls, but the oldest Pending request has waited <see cref="PendingTooLong"/>
/// (it fetches the queue and never claims from it).</item>
/// </list>
/// Silence alone isn't alerted, unlike the other funnel polls: with nothing Pending there is nothing to be late for,
/// and the producer that polls may not be deployed yet. Only Pending counts. Failed is the producer saying it gave up,
/// which the user retries from the app. Accepted, Downloading and Retrying are the producer's own progress; one it claims
/// and then loses is not watched yet (Shuffull#115).
///
/// <para>Silence is measured from the later of the last poll and <c>trackingSinceUtc</c>, the API's start, as in
/// <see cref="SignalAlertCheck"/>: no poll could arrive while the API was down. Ages are read from <c>UpdatedAt</c>,
/// because a retried Failed request goes back to Pending on its old row.</para>
/// </summary>
public sealed class SongRequestBacklogCheck(
    ShuffullContext db,
    DateTime trackingSinceUtc,
    TimeSpan? pollSilentAfter = null,
    TimeSpan? pendingTooLong = null) : IAlertCheck
{
    public static readonly TimeSpan DefaultPollSilentAfter = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan DefaultPendingTooLong = TimeSpan.FromHours(2);

    public TimeSpan PollSilentAfter { get; } = pollSilentAfter ?? DefaultPollSilentAfter;
    public TimeSpan PendingTooLong { get; } = pendingTooLong ?? DefaultPendingTooLong;

    public string KeyPrefix => "song-requests";

    public async Task<Result<IReadOnlyList<AlertFinding>>> EvaluateAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var pending = db.SongRequests.AsNoTracking().Where(r => r.Status == SongRequestStatus.Pending);
        // Nullable, so an empty queue is null, not an exception.
        var oldest = await pending.MinAsync(r => (DateTime?)r.UpdatedAt, cancellationToken);
        if (oldest == null)
        {
            return Result.Ok<IReadOnlyList<AlertFinding>>([]);
        }

        var count = await pending.CountAsync(cancellationToken);
        var lastPoll = await db.HealthSignals.AsNoTracking()
            .Where(s => s.Key == ShuffullSignalRules.SongRequestsPoll)
            .Select(s => (DateTime?)s.LastSeenUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var silentSince = lastPoll > trackingSinceUtc ? lastPoll.Value : trackingSinceUtc;
        var waiting = $"{count} song request(s) are Pending; the oldest since {oldest.Value:yyyy-MM-dd HH:mm} UTC " +
                      $"({SignalAlertCheck.Ago(nowUtc - oldest.Value)}).";

        if (nowUtc - silentSince >= PollSilentAfter)
        {
            var polled = lastPoll is { } at
                ? $"The producer last polled at {at:yyyy-MM-dd HH:mm} UTC ({SignalAlertCheck.Ago(nowUtc - at)})"
                : "The producer has never polled";
            return Result.Ok<IReadOnlyList<AlertFinding>>(
            [
                new AlertFinding("song-requests:not-polled", 2, "The funnel stopped picking up song requests",
                    $"{waiting}\n{polled}, and it should every few seconds.\n\n" +
                    "Requests wait until it does; nothing is lost. Is the funnel running, on shuffull-ingest-net, and is " +
                    "its ImportKey still right? diagnose-prod skill.",
                    ShuffullSignalRules.Daily),
            ]);
        }

        if (nowUtc - oldest.Value >= PendingTooLong)
        {
            return Result.Ok<IReadOnlyList<AlertFinding>>(
            [
                new AlertFinding("song-requests:stuck", 2, "Song requests aren't being picked up",
                    $"{waiting}\nThe producer is polling, but nothing has claimed the oldest request in " +
                    $"{PendingTooLong.TotalHours:0.#} h. A claim is an Accepted ack, so the funnel either fails to ack " +
                    "(its log says why) or skips these requests. They stay Pending until it does.",
                    ShuffullSignalRules.Daily),
            ]);
        }

        return Result.Ok<IReadOnlyList<AlertFinding>>([]);
    }
}
