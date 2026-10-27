using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence;

namespace Shuffull.Core.Services.Alerts.Checks;

/// <summary>
/// Purged songs whose media the sweep gave up deleting after <see cref="SongTombstone.MaxMediaDeleteAttempts"/> failures, in the last
/// <see cref="Window"/>. The file stays on disk (nothing is lost) and the sweep never looks at it again, so the
/// only other trace is one error line in the API's log files.
/// </summary>
public sealed class AbandonedMediaCheck(ShuffullContext db) : IAlertCheck
{
    public static readonly TimeSpan Window = TimeSpan.FromDays(30);

    public string KeyPrefix => "media";

    public async Task<Result<IReadOnlyList<AlertFinding>>> EvaluateAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var since = nowUtc - Window;
        var abandoned = await db.SongTombstones.AsNoTracking()
            .Where(t => t.MediaSweptAt != null && t.MediaSweptAt >= since && !t.MediaDeleted
                        && t.MediaSweepFailures >= SongTombstone.MaxMediaDeleteAttempts)
            .OrderBy(t => t.MediaSweptAt)
            .Select(t => new { t.SongId, t.Name, t.FileHash, t.FileExtension })
            .ToListAsync(cancellationToken);
        if (abandoned.Count == 0)
        {
            return Result.Ok<IReadOnlyList<AlertFinding>>([]);
        }

        var lines = string.Join("\n", abandoned.Take(10).Select(t => $"- {t.Name} ({t.SongId}): {t.FileHash}{t.FileExtension}"));
        var more = abandoned.Count > 10 ? $"\n…and {abandoned.Count - 10} more." : string.Empty;
        return Result.Ok<IReadOnlyList<AlertFinding>>(
        [
            new AlertFinding("media:abandoned", 1, $"The media sweep gave up on {abandoned.Count} purged song(s)",
                $"Deleting these purged songs' files failed {SongTombstone.MaxMediaDeleteAttempts} times each, so the sweep stopped trying " +
                "(in the last 30 days). The files are still on disk; nothing is lost. Check the music folder's " +
                $"permissions, then delete them by hand if they're unwanted.\n\n{lines}{more}"),
        ]);
    }
}
