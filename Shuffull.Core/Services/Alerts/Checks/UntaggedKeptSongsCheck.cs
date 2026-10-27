using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Persistence;

namespace Shuffull.Core.Services.Alerts.Checks;

/// <summary>
/// Kept songs the funnel was offered many times and never tagged. A Keep promotes a song with no tags
/// (<c>TagModel == null</c>), and the funnel fetches it from <c>songs/pending-tags</c>, which counts each hand-out in
/// <c>TagAttempts</c>. A song still untagged after <see cref="MinAttempts"/> hand-outs is failing on the funnel's
/// side every time. Nothing else drains that set, so without this it just grows.
/// </summary>
public sealed class UntaggedKeptSongsCheck(ShuffullContext db) : IAlertCheck
{
    public const int MinAttempts = 6;

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
                $"These were kept, handed to the funnel for tagging at least {MinAttempts} times, and are still untagged. " +
                "The funnel's downloader log says why (look for the song id). Untagged songs play fine but don't " +
                $"shuffle by genre.\n\n{lines}{more}"),
        ]);
    }
}
