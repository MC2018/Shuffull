namespace Shuffull.Core.Features.Listens.GetListenStats;

/// <summary>
/// One song's stats over every listen the user has logged for it.
/// </summary>
/// <param name="Plays">Every logged listen, however it ended.</param>
/// <param name="Skips">Listens that ended because the user skipped forward ("skipped"). Going back ("previous")
/// and stopping are not skips.</param>
/// <param name="AverageSecondsListened">Mean play time per listen; pauses and buffering don't count.</param>
/// <param name="CompletionPercent">Mean share of the song played per listen, 0 to 100, each listen capped at 100.
/// Only listens that carried the song's length count; null if none did.</param>
/// <param name="LastPlayed">When the most recent listen started (UTC).</param>
/// <param name="RecentQuickSkips">How many of the last <see cref="MaybeDropRule.OfLastPlays"/> plays were skips in
/// under <see cref="MaybeDropRule.SkippedUnderSeconds"/>.</param>
/// <param name="MaybeDrop">True when <paramref name="RecentQuickSkips"/> reaches <see cref="MaybeDropRule.Skips"/>.</param>
public record SongListenStats(
    string SongId,
    int Plays,
    int Skips,
    double AverageSecondsListened,
    double? CompletionPercent,
    DateTime LastPlayed,
    int RecentQuickSkips,
    bool MaybeDrop);

/// <summary>
/// Stats for every song the user has a listen for, ordered by song id. A song they no longer have (or one the
/// server purged) still appears, because listens outlive their song. <see cref="MaybeDropRule"/> is the rule the
/// flags were computed with, so the app can say what the flag means.
/// </summary>
public record GetListenStatsResponse(MaybeDropRule MaybeDropRule, IReadOnlyList<SongListenStats> Songs);
