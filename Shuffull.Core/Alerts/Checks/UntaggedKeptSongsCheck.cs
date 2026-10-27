using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Features.Songs.GetPendingTagSongs;
using Shuffull.Core.Persistence;
using Shuffull.Core.Services.Alerts;

namespace Shuffull.Core.Alerts.Checks;

/// <summary>
/// Kept songs the funnel has been offered for about two days and never tagged. A Keep promotes a song with no tags
/// (<c>TagModel == null</c>), and the funnel fetches it from <c>songs/pending-tags</c>, which counts each hand-out in
/// <c>TagAttempts</c>.
///
/// <para>A hand-out is not a failure: the funnel holds a song while its AI batch runs (up to 4 h queued, then up to
/// 24 h at OpenAI, for up to three chained batches), and the site can't tell a hold from a failure
/// (shuffull-workspace#36). So this only fires once a song has been offered for at least
/// <see cref="MinTimeOffered"/>. The funnel's own AiQueueCheck already alerts on a stuck batch queue at 12 h; this is
/// the backstop for a song that fails on the funnel's side every time.</para>
/// </summary>
public sealed class UntaggedKeptSongsCheck(ShuffullContext db) : IAlertCheck
{
    public static readonly TimeSpan MinTimeOffered = TimeSpan.FromHours(48);

    /// <summary>
    /// The hand-out count that takes at least <see cref="MinTimeOffered"/>, worked out from the real backoff
    /// (<see cref="GetPendingTagSongsHandler.RetryDelay"/>: 5 min doubling to a 1 h cap). That is 5 + 10 + 20 + 40 min
    /// for the first four, then an hour each, so the 52nd hand-out comes at least 48 h after the first: 52 today. The
    /// funnel polls every 10 minutes, so real hand-outs come a little later still.
    /// </summary>
    public static readonly int MinAttempts = AttemptsSpanning(MinTimeOffered);

    internal static int AttemptsSpanning(TimeSpan span)
    {
        var attempts = 1;
        var elapsed = TimeSpan.Zero;
        while (elapsed < span)
        {
            elapsed += GetPendingTagSongsHandler.RetryDelay(attempts);
            attempts++;
        }

        return attempts;
    }

    public string KeyPrefix => "untagged";

    public async Task<Result<IReadOnlyList<AlertFinding>>> EvaluateAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var query = db.Songs.AsNoTracking()
            .Where(s => !s.Exploratory && s.TagModel == null && !s.MetadataLocked && s.TagAttempts >= MinAttempts);
        var count = await query.CountAsync(cancellationToken);
        if (count == 0)
        {
            return Result.Ok<IReadOnlyList<AlertFinding>>([]);
        }

        var sample = await query.OrderByDescending(s => s.TagAttempts).ThenBy(s => s.SongId)
            .Select(s => new { s.SongId, s.Name, s.TagAttempts })
            .Take(10)
            .ToListAsync(cancellationToken);
        var lines = string.Join("\n", sample.Select(s => $"- {s.Name} ({s.SongId}): offered {s.TagAttempts} times"));
        var more = count > sample.Count ? $"\n…and {count - sample.Count} more." : string.Empty;

        return Result.Ok<IReadOnlyList<AlertFinding>>(
        [
            new AlertFinding("untagged:kept-songs", 1, $"{count} kept song(s) never get tagged",
                $"These were kept, handed to the funnel for tagging at least {MinAttempts} times over at least " +
                $"{MinTimeOffered.TotalHours:0} h, and are still untagged. " +
                "The funnel's downloader log says why (look for the song id). Untagged songs play fine but don't " +
                $"shuffle by genre.\n\n{lines}{more}"),
        ]);
    }
}
