using MediatR;
using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Persistence;

namespace Shuffull.Core.Features.Listens.GetListenStats;

/// <summary>
/// Derives per-song stats from the caller's listens in one query: no counters, nothing to keep in step, and a listen
/// never touches a song's or user-song's <c>Version</c>. The "last N plays" for the maybe-drop flag needs a window
/// function, which LINQ can't express on both providers, hence raw SQL. Both Postgres and SQLite run it.
///
/// <para>Cost: it reads only the caller's rows, through the <c>(UserId, SongId, StartedAt)</c> index that
/// Shuffull#60 added. A year of heavy listening (about 18,000 listens) aggregates in a few milliseconds; the
/// measured plan is in Shuffull#61's PR.</para>
/// </summary>
public class GetListenStatsHandler(ShuffullContext context)
    : IRequestHandler<GetListenStatsQuery, Result<GetListenStatsResponse>>
{
    public async Task<Result<GetListenStatsResponse>> Handle(GetListenStatsQuery request, CancellationToken cancellationToken)
    {
        var rule = request.Rule;
        var skipped = (int)ListenEndReason.Skipped;

        // "Recency" 1 is the song's latest play. ListenId breaks a tie on StartedAt so the window is stable.
        var rows = await context.Database.SqlQuery<StatsRow>(
            $"""
            SELECT "SongId",
                   COUNT(*) AS "Plays",
                   SUM(CASE WHEN "EndReason" = {skipped} THEN 1 ELSE 0 END) AS "Skips",
                   AVG("SecondsListened") AS "AverageSecondsListened",
                   AVG(CASE WHEN "SongDurationSeconds" > 0 THEN
                           CASE WHEN "SecondsListened" >= "SongDurationSeconds" THEN 1.0
                                ELSE "SecondsListened" / "SongDurationSeconds" END
                       END) AS "Completion",
                   MAX("StartedAt") AS "LastPlayed",
                   SUM(CASE WHEN "Recency" <= {rule.OfLastPlays} AND "EndReason" = {skipped}
                                 AND "SecondsListened" < {rule.SkippedUnderSeconds} THEN 1 ELSE 0 END) AS "RecentQuickSkips"
            FROM (
                SELECT "SongId", "StartedAt", "SecondsListened", "SongDurationSeconds", "EndReason",
                       ROW_NUMBER() OVER (PARTITION BY "SongId" ORDER BY "StartedAt" DESC, "ListenId" DESC) AS "Recency"
                FROM "Listens"
                WHERE "UserId" = {request.UserId}
            ) AS l
            GROUP BY "SongId"
            ORDER BY "SongId"
            """)
            .ToListAsync(cancellationToken);

        var songs = rows
            .Select(r => new SongListenStats(
                SongId: r.SongId,
                Plays: (int)r.Plays,
                Skips: (int)r.Skips,
                AverageSecondsListened: r.AverageSecondsListened,
                CompletionPercent: r.Completion * 100,
                // SQLite hands the timestamp back unspecified; it was stored as UTC.
                LastPlayed: DateTime.SpecifyKind(r.LastPlayed, DateTimeKind.Utc),
                RecentQuickSkips: (int)r.RecentQuickSkips,
                MaybeDrop: r.RecentQuickSkips >= rule.Skips))
            .ToList();

        return Result.Ok(new GetListenStatsResponse(rule, songs));
    }

    // Postgres returns COUNT and SUM as bigint, hence long.
    private sealed class StatsRow
    {
        public string SongId { get; set; } = "";
        public long Plays { get; set; }
        public long Skips { get; set; }
        public double AverageSecondsListened { get; set; }
        public double? Completion { get; set; }
        public DateTime LastPlayed { get; set; }
        public long RecentQuickSkips { get; set; }
    }
}
