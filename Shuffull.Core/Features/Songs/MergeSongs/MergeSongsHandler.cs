using MediatR;
using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Persistence;

namespace Shuffull.Core.Features.Songs.MergeSongs;

/// <summary>
/// Folds the loser into the survivor and deletes it, in one transaction. Nothing a user decided is lost:
/// <list type="bullet">
/// <item><c>UserSong</c>: one row per user on the survivor. The stronger <see cref="LikeStatus"/> wins (Love, then
/// Like, then Dislike, then Neutral, so a Dislike survives only when neither side is liked) and so does the later
/// <c>LastPlayed</c>.</item>
/// <item><c>PlaylistSong</c>: re-pointed, or dropped when that playlist already holds the survivor. A playlist's
/// current song is re-pointed too.</item>
/// <item>Listens, replacement requests, YouTube rating requests and import records: re-pointed. When both songs have
/// an open replacement request, the survivor's stays and the loser's is dropped.</item>
/// <item>The survivor stops being an audition song if either side was kept or anyone likes either. A merge never
/// turns a kept song back into an audition.</item>
/// </list>
/// The loser leaves a <see cref="SongTombstone"/>, so its media goes through the same 7-day sweep as a purge, and a
/// <see cref="SongMerge"/> row for devices and the importer.
/// <para>
/// Duplicate reports follow the loser too. A report on this pair that still hides a copy closes as merged: the merge
/// has decided which copy stays. Any other report naming the loser is re-pointed at the survivor, so a reporter's hide
/// keeps hiding the same recording. Where that reporter already has a report on the new pair, the newer of the two
/// decisions wins and the other id becomes an alias of it, as when the app sends a second id for one pair.
/// </para>
/// <para>
/// Every row whose Version changes gets its own millisecond, so the app's 500-row Version paging never sees a tie.
/// </para>
/// <para>
/// Merges run one at a time (a transaction-scoped advisory lock), and each starts its Versions after the last merge's.
/// So <see cref="SongMerge.Version"/> follows commit order, and a device paging the feed can't move past a merge
/// that commits later with an earlier Version. A merge row is never sent again, so missing one would be for good.
/// </para>
/// <para>
/// Both song rows are locked first (<c>FOR UPDATE</c>), then their <c>UserSong</c> rows, as
/// <c>DeletePlaylistHandler</c> does. A Keep, Like or library add that commits first is read and moved. One that
/// arrives later waits for the merge, then finds the loser gone instead of being silently cascaded away. The song
/// rows go first because that is the order a promoting Like writes them in, so the two can't deadlock.
/// </para>
/// </summary>
public class MergeSongsHandler(ShuffullContext context)
    : IRequestHandler<MergeSongsCommand, Result<MergeSongsResponse>>
{
    /// <summary>The advisory lock key that serializes merges ("SMRG").</summary>
    public const long MergeLockKey = 0x534D_5247;

    public async Task<Result<MergeSongsResponse>> Handle(MergeSongsCommand request, CancellationToken cancellationToken)
    {
        // The validator checks these; this guard keeps the handler safe when called without it.
        if (string.IsNullOrEmpty(request.SurvivorSongId) || string.IsNullOrEmpty(request.LoserSongId)
            || request.SurvivorSongId == request.LoserSongId)
        {
            return Result.Error<MergeSongsResponse>(
                new KindedError(ErrorKind.Invalid, "survivorSongId and loserSongId must name two different songs."));
        }

        var survivorId = request.SurvivorSongId;
        var loserId = request.LoserSongId;

        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            await LockRowsAsync(survivorId, loserId, cancellationToken);

            var songs = await context.Songs
                .Where(s => s.SongId == survivorId || s.SongId == loserId)
                .ToListAsync(cancellationToken);
            var survivor = songs.FirstOrDefault(s => s.SongId == survivorId);
            var loser = songs.FirstOrDefault(s => s.SongId == loserId);

            if (loser is null)
            {
                var earlier = await context.SongMerges.AsNoTracking().FirstOrDefaultAsync(m => m.LoserSongId == loserId, cancellationToken);
                if (earlier is not null && earlier.SurvivorSongId == survivorId)
                {
                    // A resend of a merge that already happened.
                    return Result.Ok(new MergeSongsResponse(SongMergeDto.Create(earlier), [], AlreadyMerged: true));
                }
                return Result.Error<MergeSongsResponse>(earlier is not null
                    ? ResultErrors.Conflict($"Song '{loserId}' was already merged into '{earlier.SurvivorSongId}'.")
                    : ResultErrors.NotFound($"Song '{loserId}' was not found."));
            }

            if (survivor is null)
            {
                var earlier = await context.SongMerges.AsNoTracking().FirstOrDefaultAsync(m => m.LoserSongId == survivorId, cancellationToken);
                return Result.Error<MergeSongsResponse>(earlier is not null
                    ? ResultErrors.Conflict($"Song '{survivorId}' was itself merged into '{earlier.SurvivorSongId}'. Merge into that song instead.")
                    : ResultErrors.NotFound($"Song '{survivorId}' was not found."));
            }

            var now = DateTime.UtcNow;
            var lastMerge = await context.SongMerges.MaxAsync(m => (DateTime?)m.Version, cancellationToken);
            var versions = new VersionClock(lastMerge is { } last && last >= now ? last.AddMilliseconds(1) : now);
            var affectedUserIds = new HashSet<string>();

            // Sentiment and play history: one row per user, on the survivor.
            var userSongs = await context.UserSongs
                .Where(us => us.SongId == survivorId || us.SongId == loserId)
                .ToListAsync(cancellationToken);
            var anyoneLikes = userSongs.Any(us => us.LikeStatus.RetainsSong());
            foreach (var mine in userSongs.GroupBy(us => us.UserId).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var onLoser = mine.FirstOrDefault(us => us.SongId == loserId);
                if (onLoser is null)
                {
                    continue;
                }

                var onSurvivor = mine.FirstOrDefault(us => us.SongId == survivorId);
                if (onSurvivor is null)
                {
                    onSurvivor = new UserSong
                    {
                        UserId = onLoser.UserId,
                        SongId = survivorId,
                        LastPlayed = onLoser.LastPlayed,
                        LikeStatus = onLoser.LikeStatus,
                    };
                    context.UserSongs.Add(onSurvivor);
                }
                else
                {
                    onSurvivor.LikeStatus = Stronger(onSurvivor.LikeStatus, onLoser.LikeStatus);
                    onSurvivor.LastPlayed = onSurvivor.LastPlayed >= onLoser.LastPlayed ? onSurvivor.LastPlayed : onLoser.LastPlayed;
                }
                onSurvivor.Version = versions.Next();
                context.UserSongs.Remove(onLoser);
                affectedUserIds.Add(onLoser.UserId);
            }

            // Playlists: one entry per playlist for the song, and the current song follows it.
            var affectedPlaylistIds = new HashSet<string>();
            var playlistsHoldingSurvivor = (await context.PlaylistSongs
                .Where(ps => ps.SongId == survivorId)
                .Select(ps => ps.PlaylistId)
                .ToListAsync(cancellationToken)).ToHashSet();
            var loserEntries = await context.PlaylistSongs
                .Where(ps => ps.SongId == loserId)
                .OrderBy(ps => ps.PlaylistSongId)
                .ToListAsync(cancellationToken);
            foreach (var entry in loserEntries)
            {
                if (playlistsHoldingSurvivor.Add(entry.PlaylistId))
                {
                    entry.SongId = survivorId;
                }
                else
                {
                    context.PlaylistSongs.Remove(entry);
                }
                affectedPlaylistIds.Add(entry.PlaylistId);
            }
            var playingLoser = await context.Playlists.Where(p => p.CurrentSongId == loserId).ToListAsync(cancellationToken);
            foreach (var playlist in playingLoser)
            {
                playlist.CurrentSongId = survivorId;
                affectedPlaylistIds.Add(playlist.PlaylistId);
            }
            var playlists = await context.Playlists
                .Where(p => affectedPlaylistIds.Contains(p.PlaylistId))
                .ToListAsync(cancellationToken);
            foreach (var playlist in playlists.OrderBy(p => p.PlaylistId, StringComparer.Ordinal))
            {
                playlist.Version = versions.Next();
                affectedUserIds.Add(playlist.UserId);
            }

            // Re-source requests: the survivor keeps at most one open request.
            var survivorHasOpenReplacement = await context.SongReplacements
                .AnyAsync(r => r.SongId == survivorId && r.Status != SongReplacementStatus.Completed, cancellationToken);
            var loserReplacements = await context.SongReplacements
                .Where(r => r.SongId == loserId)
                .OrderBy(r => r.CreatedAt)
                .ToListAsync(cancellationToken);
            foreach (var replacement in loserReplacements)
            {
                if (replacement.Status == SongReplacementStatus.Completed)
                {
                    replacement.SongId = survivorId;
                }
                else if (survivorHasOpenReplacement)
                {
                    context.SongReplacements.Remove(replacement);
                }
                else
                {
                    replacement.SongId = survivorId;
                    // The request now asks to re-source the survivor, whose source this is.
                    replacement.OriginalExternalSongId = survivor.ExternalSongId;
                    survivorHasOpenReplacement = true;
                }
            }

            // A merge never turns a kept or liked song back into an audition.
            survivor.Exploratory = survivor.Exploratory && loser.Exploratory && !anyoneLikes;
            survivor.Version = versions.Next();

            var merge = new SongMerge
            {
                LoserSongId = loserId,
                SurvivorSongId = survivorId,
                LoserExternalSongId = loser.ExternalSongId,
                LoserFileHash = loser.FileHash,
                MergedByUserId = request.UserId,
                MergedAt = now,
                Version = versions.Next(),
            };
            context.SongMerges.Add(merge);

            // Media is left on disk: the tombstone hands it to PurgedSongMediaSweepService, which waits out the grace
            // window and keeps the file if a live song still shares its hash.
            context.SongTombstones.Add(new SongTombstone
            {
                SongId = loserId,
                Name = loser.Name,
                ExternalSongId = loser.ExternalSongId,
                FileHash = loser.FileHash,
                FileExtension = loser.FileExtension,
                DeletedByUserId = request.UserId,
                PlaylistId = null,
                DeletedAt = now,
            });

            var closed = await FollowReportsAsync(request.UserId, survivorId, loserId, now, versions, cancellationToken);

            // Every affected user's sync notices the change.
            var users = await context.Users.Where(u => affectedUserIds.Contains(u.UserId)).ToListAsync(cancellationToken);
            foreach (var user in users.OrderBy(u => u.UserId, StringComparer.Ordinal))
            {
                user.Version = versions.Next();
            }

            await context.SaveChangesAsync(cancellationToken);

            // History that points at the song by id.
            await context.Listens.Where(l => l.SongId == loserId)
                .ExecuteUpdateAsync(set => set.SetProperty(l => l.SongId, survivorId), cancellationToken);
            await context.YoutubeRatingRequests.Where(r => r.SongId == loserId)
                .ExecuteUpdateAsync(set => set.SetProperty(r => r.SongId, survivorId), cancellationToken);
            await context.SongImports.Where(si => si.SongId == loserId)
                .ExecuteUpdateAsync(set => set.SetProperty(si => si.SongId, (string?)survivorId), cancellationToken);

            // The loser's own metadata goes with it. Everything users own was moved above, and the rows are locked, so
            // the song delete has nothing left to cascade.
            await context.SongArtists.Where(sa => sa.SongId == loserId).ExecuteDeleteAsync(cancellationToken);
            await context.SongTags.Where(st => st.SongId == loserId).ExecuteDeleteAsync(cancellationToken);
            await context.Songs.Where(s => s.SongId == loserId).ExecuteDeleteAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return Result.Ok(new MergeSongsResponse(
                SongMergeDto.Create(merge),
                closed.Select(SongDuplicateReportDto.Create).ToList(),
                AlreadyMerged: false));
        }
        catch (Exception ex)
        {
            // Disposing the transaction rolls every statement back, so nothing was merged and a resend starts over.
            context.ChangeTracker.Clear();
            return Result.Error<MergeSongsResponse>(
                ResultErrors.FromException(ex, $"Could not merge the songs; nothing was changed. {ex.Message}"));
        }
    }

    /// <summary>Love beats Like beats Dislike beats Neutral.</summary>
    public static LikeStatus Stronger(LikeStatus a, LikeStatus b) => Rank(a) >= Rank(b) ? a : b;

    private static int Rank(LikeStatus status) => status switch
    {
        LikeStatus.Love => 3,
        LikeStatus.Like => 2,
        LikeStatus.Dislike => 1,
        _ => 0,
    };

    /// <summary>
    /// Closes the reports on the pair that still hide a copy, and re-points every other report naming the loser at the
    /// survivor. Returns the closed ones. Merged reports elsewhere are history and stay as they are. Withdrawn ones are
    /// re-pointed too, so a resend the app maps to the survivor still finds its report.
    /// </summary>
    private async Task<List<SongDuplicateReport>> FollowReportsAsync(
        string curatorId, string survivorId, string loserId, DateTime now, VersionClock versions, CancellationToken cancellationToken)
    {
        // A report's preferred and hidden songs are always its SongId or OtherSongId.
        var pairKey = SongDuplicateReport.PairKeyOf(survivorId, loserId);
        var naming = await context.SongDuplicateReports
            .Where(r => r.SongId == loserId || r.OtherSongId == loserId)
            .OrderBy(r => r.CreatedAt)
            .ThenBy(r => r.ReportId)
            .ToListAsync(cancellationToken);

        var closed = new List<SongDuplicateReport>();
        foreach (var report in naming.Where(r => r.PairKey == pairKey && HidesSomething(r.Status)))
        {
            report.Status = SongDuplicateReportStatus.Merged;
            report.ResolvedAt = now;
            report.ResolvedByUserId = curatorId;
            report.Version = versions.Next();
            closed.Add(report);
        }

        string Swap(string id) => id == loserId ? survivorId : id;
        var moving = naming
            .Where(r => r.PairKey != pairKey && r.Status != SongDuplicateReportStatus.Merged)
            .Select(r => (Report: r, PairKey: SongDuplicateReport.PairKeyOf(Swap(r.SongId), r.OtherSongId is null ? null : Swap(r.OtherSongId))))
            .ToList();
        if (moving.Count == 0)
        {
            return closed;
        }

        var reporterIds = moving.Select(m => m.Report.ReporterUserId).Distinct().ToList();
        var newPairKeys = moving.Select(m => m.PairKey).Distinct().ToList();
        var onNewPairs = (await context.SongDuplicateReports
                .Where(r => reporterIds.Contains(r.ReporterUserId) && newPairKeys.Contains(r.PairKey))
                .ToListAsync(cancellationToken))
            .ToDictionary(r => (r.ReporterUserId, r.PairKey));

        foreach (var (report, newPairKey) in moving)
        {
            if (!onNewPairs.TryGetValue((report.ReporterUserId, newPairKey), out var kept))
            {
                CopyDecision(report, report);
                report.PairKey = newPairKey;
                report.Version = versions.Next();
                continue;
            }

            // The reporter already reported the survivor's pair. The newer decision wins, unless that report is merged.
            if (kept.Status != SongDuplicateReportStatus.Merged && report.Version > kept.Version)
            {
                CopyDecision(report, kept);
                kept.Version = versions.Next();
            }
            context.SongDuplicateReportAliases.Add(new SongDuplicateReportAlias { AliasId = report.ReportId, ReportId = kept.ReportId });
            context.SongDuplicateReports.Remove(report);

            // Aliases of the folded report follow it.
            foreach (var alias in await context.SongDuplicateReportAliases.Where(a => a.ReportId == report.ReportId).ToListAsync(cancellationToken))
            {
                alias.ReportId = kept.ReportId;
            }
        }

        return closed;

        void CopyDecision(SongDuplicateReport from, SongDuplicateReport to)
        {
            to.SongId = Swap(from.SongId);
            to.OtherSongId = from.OtherSongId is null ? null : Swap(from.OtherSongId);
            to.Kind = from.Kind;
            to.PreferredSongId = from.PreferredSongId is null ? null : Swap(from.PreferredSongId);
            to.HiddenSongId = Swap(from.HiddenSongId);
            to.Note = from.Note;
            to.Status = from.Status;
            to.ResolvedAt = from.ResolvedAt;
            to.ResolvedByUserId = from.ResolvedByUserId;
        }
    }

    /// <summary>The statuses whose report hides a copy for its reporter.</summary>
    private static bool HidesSomething(SongDuplicateReportStatus status) => status is SongDuplicateReportStatus.Pending
        or SongDuplicateReportStatus.NotDuplicate or SongDuplicateReportStatus.KeptBoth;

    /// <summary>
    /// Takes the merge lock, then locks both song rows and their <c>UserSong</c> rows, each in key order. Locking the
    /// song rows makes an insert that references the loser (a library add, a playlist add) wait and then fail, rather
    /// than be cascaded away by the delete. Locking the <c>UserSong</c> rows makes a Like or Keep on them commit before
    /// they are read, or wait until the merge is done. All of it is Postgres only (see <see cref="SongRowLocks"/>).
    /// </summary>
    private async Task LockRowsAsync(string survivorId, string loserId, CancellationToken cancellationToken)
    {
        if (!SongRowLocks.IsPostgres(context))
        {
            return;
        }

        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({MergeLockKey})", cancellationToken);
        var ids = new[] { survivorId, loserId };
        await SongRowLocks.LockSongsAsync(context, ids, cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"UserSongs\" WHERE \"SongId\" = ANY({ids}) ORDER BY \"UserId\", \"SongId\" FOR UPDATE",
            cancellationToken);
    }

    /// <summary>Hands out one Version per row, a millisecond apart, starting at the current millisecond.</summary>
    private sealed class VersionClock(DateTime now)
    {
        private readonly DateTime _start = new(now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
        private int _issued;

        public DateTime Next() => _start.AddMilliseconds(_issued++);
    }
}
