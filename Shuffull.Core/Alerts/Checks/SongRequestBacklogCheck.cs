using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Persistence;
using Shuffull.Core.Services.Alerts;

namespace Shuffull.Core.Alerts.Checks;

/// <summary>
/// Song requests (Shuffull#57) left waiting for the producer (Shuffull#112, Shuffull#115). The funnel polls
/// <c>GET song-requests?status=Pending</c> every few seconds and claims each request with an Accepted ack, so a
/// request should leave Pending almost at once. Three ways it doesn't get through, each its own alert:
/// <list type="bullet">
/// <item><b>not polled</b>: requests are Pending and the producer hasn't polled for <see cref="PollSilentAfter"/>
/// (it is down, or can't reach the API: the like-sync once sat a month that way, off shuffull-ingest-net);</item>
/// <item><b>stuck</b>: the producer polls, but the oldest Pending request has waited <see cref="PendingTooLong"/>
/// (it fetches the queue and never claims from it);</item>
/// <item><b>no progress</b>: a request the producer claimed (Accepted, Downloading, Retrying) hasn't moved for
/// <see cref="NoProgressAfter"/>. Only the producer can move it on (<c>SongRequestRules.AckMayFollow</c>), so one it
/// loses stays open forever: the user can't retry it, and it counts against their open-request cap.</item>
/// </list>
/// While <b>not polled</b> fires it is the only finding: the producer is down, so its claimed requests not moving is
/// the same fault. <b>Stuck</b> and <b>no progress</b> can fire together; claiming and the download lane are separate
/// steps in the funnel and can break separately. This check only alerts. It never fails a claimed request itself,
/// because the producer may still finish it.
///
/// <para>Silence alone isn't alerted, unlike the other funnel polls: with nothing open there is nothing to be late
/// for, and the producer that polls may not be deployed yet. Failed isn't watched: it is the producer saying it gave
/// up, which the user retries from the app.</para>
///
/// <para>Silence and lack of progress are measured from no earlier than <c>trackingSinceUtc</c>, the API's start, as
/// in <see cref="SignalAlertCheck"/>: no poll or ack could arrive while the API was down. Ages are read from
/// <c>UpdatedAt</c>, which every ack sets (a re-sent one too), and because a retried Failed request goes back to
/// Pending on its old row.</para>
/// </summary>
public sealed class SongRequestBacklogCheck(
    ShuffullContext db,
    DateTime trackingSinceUtc,
    TimeSpan? pollSilentAfter = null,
    TimeSpan? pendingTooLong = null,
    TimeSpan? noProgressAfter = null) : IAlertCheck
{
    public static readonly TimeSpan DefaultPollSilentAfter = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan DefaultPendingTooLong = TimeSpan.FromHours(2);

    /// <summary>
    /// The funnel's request lane (YoutubeFunnel#20) moves a request out of the claimed states within minutes: it works
    /// one request at a time, races each for at most 3 minutes in the foreground before failing it, and retries a
    /// refused export at most 30 minutes apart. Even a user's full queue of 50 clears in a few hours. 6 hours leaves room
    /// for that and for a slow funnel redeploy.
    /// </summary>
    public static readonly TimeSpan DefaultNoProgressAfter = TimeSpan.FromHours(6);

    /// <summary>The states the producer moves a request through after claiming it, before it completes or fails.</summary>
    public static readonly SongRequestStatus[] Claimed =
    [
        SongRequestStatus.Accepted,
        SongRequestStatus.Downloading,
        SongRequestStatus.Retrying,
    ];

    public TimeSpan PollSilentAfter { get; } = pollSilentAfter ?? DefaultPollSilentAfter;
    public TimeSpan PendingTooLong { get; } = pendingTooLong ?? DefaultPendingTooLong;
    public TimeSpan NoProgressAfter { get; } = noProgressAfter ?? DefaultNoProgressAfter;

    public string KeyPrefix => "song-requests";

    public async Task<Result<IReadOnlyList<AlertFinding>>> EvaluateAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var lastPoll = await db.HealthSignals.AsNoTracking()
            .Where(s => s.Key == ShuffullSignalRules.SongRequestsPoll)
            .Select(s => (DateTime?)s.LastSeenUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var silentSince = lastPoll > trackingSinceUtc ? lastPoll.Value : trackingSinceUtc;
        var polled = lastPoll is { } at
            ? $"The producer last polled at {at:yyyy-MM-dd HH:mm} UTC ({SignalAlertCheck.Ago(nowUtc - at)})"
            : "The producer has never polled";

        var findings = new List<AlertFinding>();
        var pending = db.SongRequests.AsNoTracking().Where(r => r.Status == SongRequestStatus.Pending);
        // Nullable, so an empty queue is null, not an exception.
        var oldest = await pending.MinAsync(r => (DateTime?)r.UpdatedAt, cancellationToken);
        if (oldest != null)
        {
            var count = await pending.CountAsync(cancellationToken);
            var waiting = $"{count} song request(s) are Pending; the oldest since {oldest.Value:yyyy-MM-dd HH:mm} UTC " +
                          $"({SignalAlertCheck.Ago(nowUtc - oldest.Value)}).";

            if (nowUtc - silentSince >= PollSilentAfter)
            {
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
                findings.Add(new AlertFinding("song-requests:stuck", 2, "Song requests aren't being picked up",
                    $"{waiting}\nThe producer is polling, but nothing has claimed the oldest request in " +
                    $"{PendingTooLong.TotalHours:0.#} h. A claim is an Accepted ack, so the funnel either fails to ack " +
                    "(its log says why) or skips these requests. They stay Pending until it does.",
                    ShuffullSignalRules.Daily));
            }
        }

        var noProgress = await NoProgressAsync(nowUtc, polled, cancellationToken);
        if (noProgress != null)
        {
            findings.Add(noProgress);
        }

        return Result.Ok<IReadOnlyList<AlertFinding>>(findings);
    }

    private async Task<AlertFinding?> NoProgressAsync(DateTime nowUtc, string polled, CancellationToken cancellationToken)
    {
        // No ack could arrive while the API was down, so a request isn't late until the window has passed since it started.
        if (nowUtc - trackingSinceUtc < NoProgressAfter)
        {
            return null;
        }

        var before = nowUtc - NoProgressAfter;
        var stalled = db.SongRequests.AsNoTracking().Where(r => Claimed.Contains(r.Status) && r.UpdatedAt <= before);
        var oldest = await stalled.MinAsync(r => (DateTime?)r.UpdatedAt, cancellationToken);
        if (oldest == null)
        {
            return null;
        }

        var byStatus = await stalled.GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var count = byStatus.Sum(s => s.Count);
        var breakdown = string.Join(", ", byStatus.OrderBy(s => s.Status).Select(s => $"{s.Count} {s.Status}"));

        return new AlertFinding("song-requests:no-progress", 2, $"{count} song request(s) stopped making progress",
            $"{count} song request(s) the producer claimed haven't moved in {NoProgressAfter.TotalHours:0.#} h ({breakdown}); " +
            $"the oldest since {oldest.Value:yyyy-MM-dd HH:mm} UTC ({SignalAlertCheck.Ago(nowUtc - oldest.Value)}).\n" +
            $"{polled}.\n\n" +
            "The funnel's request lane finishes or fails a request within minutes, so these were most likely lost: a " +
            "funnel bug, a reset Video row, or an ack the API refused (the funnel's log says which). Until the funnel acks " +
            "them they stay open: the user can't request them again, and they count against the open-request cap. Look up " +
            "each Video row by its ExternalSongId in the funnel DB; diagnose-prod skill.",
            ShuffullSignalRules.Daily);
    }
}
