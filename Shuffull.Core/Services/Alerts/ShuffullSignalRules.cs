namespace Shuffull.Core.Services.Alerts;

/// <summary>
/// The signal keys the site records and when each one alerts. A key recorded somewhere but missing here never
/// alerts, so add the rule with the recording.
/// </summary>
public static class ShuffullSignalRules
{
    public static readonly TimeSpan Daily = TimeSpan.FromDays(1);

    public const string FunnelProbe = "probe:funnel";
    public const string PendingTagsPoll = "funnel-poll:pending-tags";
    public const string YoutubeRatingsPoll = "funnel-poll:youtube-ratings";
    public const string KeepRefused = "keep-refused";
    public const string MediaSweep = "media-sweep";

    public static readonly IReadOnlyList<SignalRule> All =
    [
        new(FunnelProbe, _ => "The funnel is down",
            "The funnel's web API (youtubefunnel.web-api) didn't answer its health check 3 times in a row over " +
            "shuffull-ingest-net. Either the YoutubeFunnel stack is down or it left that network. diagnose-prod skill.",
            MinFailures: 3, RemindEvery: Daily),

        // Only a success is ever recorded for the two polls (each time the funnel calls), so these watch for silence.
        new(PendingTagsPoll, null,
            "The funnel calls GET songs/pending-tags every few seconds to fetch kept songs to tag; it hasn't for an hour. " +
            "Kept songs stay untagged meanwhile. Is the funnel's downloader running, and is its ImportKey still right?",
            RemindEvery: Daily, StaleAfter: TimeSpan.FromHours(1),
            StaleSubject: _ => "The funnel stopped picking up songs to tag"),

        new(YoutubeRatingsPoll, null,
            "The funnel's youtube worker fetches pending likes every 10 minutes; it hasn't for an hour, so likes aren't " +
            "reaching YouTube. Is youtubefunnel.youtube running and on shuffull-ingest-net?",
            RemindEvery: Daily, StaleAfter: TimeSpan.FromHours(1),
            StaleSubject: _ => "The funnel stopped syncing likes to YouTube"),

        new(KeepRefused, _ => "A Keep was refused",
            "The server refused to record a Keep, usually because the song isn't in that user's library. The app holds " +
            "the playlist's delete while the Keep is outstanding (ShuffullApp#109 is the open question of what to do " +
            "then), and nothing in the app shows it.",
            Level: 1, RecentWithin: Daily),

        new(MediaSweep, _ => "The purged-song media sweep keeps failing",
            "Each pass of PurgedSongMediaSweepService throws, so purged songs' media isn't being removed. Nothing is " +
            "lost; disk use grows. The error is in the API's log files.",
            MinFailures: 3, FailingFor: TimeSpan.FromHours(3), Level: 1, RemindEvery: Daily),

        new("monitor:", s => $"The '{SignalRule.Suffix(s)}' alert check can't run",
            "The alert monitor itself can't run this check, so it can't warn you about what the check covers. See the " +
            "API's log files.",
            MinFailures: 3, FailingFor: TimeSpan.FromHours(1), Level: 1, RemindEvery: Daily),
    ];
}
