using MediatR;
using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Persistence;

namespace Shuffull.Core.Features.SongDuplicateReports.ReportSongDuplicates;

/// <summary>
/// Stores the caller's duplicate reports, one outcome per item, in one save. Each report is the user's decision and
/// its own row: nothing here waits on a curator or touches the songs or the user's like status.
///
/// <list type="bullet">
/// <item>Both songs must exist and be in the caller's library, or the item is rejected.</item>
/// <item>A report id already stored is a resend: its row takes the item's fields again (so nothing changes), and a
/// withdrawn report is reopened. A curator's verdict on it stands.</item>
/// <item>A new id on a pair the caller already reported updates that report instead of adding a second one, and
/// reopens it if it was withdrawn or resolved without a merge: the user is asking again. The new id is kept as an
/// alias of that report, so a withdraw or resend the outbox queued under it still finds it.</item>
/// <item>A merged report is never changed here.</item>
/// </list>
///
/// <para>A failure saves nothing and fails the whole batch, so the outbox keeps every row and sends them again. That
/// includes losing a race with another device creating the same pair (409): the resend then updates that report.</para>
/// </summary>
public class ReportSongDuplicatesHandler(ShuffullContext context)
    : IRequestHandler<ReportSongDuplicatesCommand, Result<ReportSongDuplicatesResponse>>
{
    /// <summary>Matches the app's outbox batch size.</summary>
    public const int MaxBatch = 200;

    public async Task<Result<ReportSongDuplicatesResponse>> Handle(ReportSongDuplicatesCommand request, CancellationToken cancellationToken)
    {
        var items = request.Reports ?? [];
        if (items.Count == 0)
        {
            return Result.Ok(new ReportSongDuplicatesResponse([]));
        }

        try
        {
            var songIds = items
                .SelectMany(i => new[] { i?.SongId, NullIfEmpty(i?.OtherSongId) })
                .OfType<string>()
                .Where(IsUsableId)
                .Distinct()
                .ToList();
            var known = (await context.Songs
                .Where(s => songIds.Contains(s.SongId))
                .Select(s => s.SongId)
                .ToListAsync(cancellationToken)).ToHashSet();
            var owned = (await context.UserSongs
                .Where(us => us.UserId == request.UserId && songIds.Contains(us.SongId))
                .Select(us => us.SongId)
                .ToListAsync(cancellationToken)).ToHashSet();

            var reportIds = items.Select(i => i?.ReportId).OfType<string>().Where(IsUsableId).Distinct().ToList();
            var aliases = await context.SongDuplicateReportAliases
                .Where(a => reportIds.Contains(a.AliasId))
                .ToListAsync(cancellationToken);
            var aliasTargets = aliases.Select(a => a.ReportId).Distinct().ToList();
            var pairKeys = items
                .Where(i => i?.SongId is not null)
                .Select(i => SongDuplicateReport.PairKeyOf(i!.SongId!, NullIfEmpty(i.OtherSongId)))
                .Distinct()
                .ToList();
            var existing = await context.SongDuplicateReports
                .Where(r => reportIds.Contains(r.ReportId)
                    || aliasTargets.Contains(r.ReportId)
                    || (r.ReporterUserId == request.UserId && pairKeys.Contains(r.PairKey)))
                .ToListAsync(cancellationToken);

            var byId = existing.ToDictionary(r => r.ReportId);
            var batch = new Batch(
                request.UserId,
                known,
                owned,
                byId,
                aliases.ToDictionary(a => a.AliasId, a => byId[a.ReportId]),
                existing.Where(r => r.ReporterUserId == request.UserId).ToDictionary(r => r.PairKey),
                SongDuplicateReport.VersionNow());

            var applied = items.Select(item => Apply(batch, item)).ToList();
            await context.SaveChangesAsync(cancellationToken);

            // Built after the save, so an item that a later one in the batch updated reports the row as stored.
            return Result.Ok(new ReportSongDuplicatesResponse(applied
                .Select(a => new SongDuplicateReportResult(
                    a.ReportId, a.Outcome, a.Error, a.Row is null ? null : SongDuplicateReportDto.Create(a.Row)))
                .ToList()));
        }
        catch (Exception ex)
        {
            return Result.Error<ReportSongDuplicatesResponse>(
                ResultErrors.FromException(ex, $"Could not record the duplicate reports; none were recorded. {ex.Message}"));
        }
    }

    /// <summary>What one request has loaded, and the rows and aliases it adds as it goes.</summary>
    private sealed class Batch(
        string userId,
        HashSet<string> knownSongs,
        HashSet<string> ownedSongs,
        Dictionary<string, SongDuplicateReport> byId,
        Dictionary<string, SongDuplicateReport> byAlias,
        Dictionary<string, SongDuplicateReport> byPair,
        DateTime now)
    {
        private int _written;

        public string UserId { get; } = userId;
        public HashSet<string> KnownSongs { get; } = knownSongs;
        public HashSet<string> OwnedSongs { get; } = ownedSongs;
        public Dictionary<string, SongDuplicateReport> ById { get; } = byId;
        public Dictionary<string, SongDuplicateReport> ByAlias { get; } = byAlias;
        public Dictionary<string, SongDuplicateReport> ByPair { get; } = byPair;
        public DateTime Now { get; } = now;

        /// <summary>Each row written gets its own Version, a microsecond apart.</summary>
        public DateTime NextVersion() => Now.AddTicks(10 * _written++);
    }

    private sealed record Applied(string? ReportId, string Outcome, string? Error = null, SongDuplicateReport? Row = null);

    private Applied Apply(Batch batch, SongDuplicateReportItem? item)
    {
        if (item is null)
        {
            return Reject(null, "Report is required.");
        }

        var reportId = item.ReportId;
        if (CheckId(reportId, "reportId") is { } badId)
        {
            return Reject(reportId, badId);
        }
        if (CheckId(item.SongId, "songId") is { } badSong)
        {
            return Reject(reportId, badSong);
        }

        var songId = item.SongId!;
        var otherSongId = NullIfEmpty(item.OtherSongId);
        if (otherSongId is not null && CheckId(otherSongId, "otherSongId") is { } badOther)
        {
            return Reject(reportId, badOther);
        }
        if (otherSongId == songId)
        {
            return Reject(reportId, "otherSongId must be a different song from songId.");
        }

        if (EnumNames.Parse<SongDuplicateReportKind>(item.Kind) is not { } kind)
        {
            return Reject(reportId, $"Unknown kind '{item.Kind}'.");
        }
        if (kind == SongDuplicateReportKind.Duplicate && otherSongId is null)
        {
            return Reject(reportId, "A duplicate report needs otherSongId.");
        }

        var preferredSongId = NullIfEmpty(item.PreferredSongId);
        if (preferredSongId is not null && otherSongId is null)
        {
            // With one song there is nothing to choose between, and keeping it would mean hiding it.
            return Reject(reportId, "preferredSongId needs otherSongId.");
        }
        if (preferredSongId is not null && preferredSongId != songId && preferredSongId != otherSongId)
        {
            return Reject(reportId, "preferredSongId must be one of the reported songs.");
        }

        var note = string.IsNullOrWhiteSpace(item.Note) ? null : item.Note.Trim();
        if (note is { Length: > SongDuplicateReport.MaxNoteLength })
        {
            return Reject(reportId, $"note is longer than {SongDuplicateReport.MaxNoteLength} characters.");
        }
        if (note?.Contains('\0') == true)
        {
            return Reject(reportId, "note can't contain a NUL character.");
        }

        foreach (var id in otherSongId is null ? new[] { songId } : new[] { songId, otherSongId })
        {
            if (!batch.KnownSongs.Contains(id))
            {
                return Reject(reportId, $"Unknown song '{id}'.");
            }
            if (!batch.OwnedSongs.Contains(id))
            {
                return Reject(reportId, $"Song '{id}' is not in your library.");
            }
        }

        var pairKey = SongDuplicateReport.PairKeyOf(songId, otherSongId);
        string outcome;
        bool reopen;
        if (batch.ById.TryGetValue(reportId!, out var row))
        {
            if (row.ReporterUserId != batch.UserId || row.PairKey != pairKey)
            {
                return Reject(reportId, "This reportId is already used by another report.");
            }

            outcome = SongDuplicateReportOutcomes.Recorded;
            reopen = row.Status == SongDuplicateReportStatus.Withdrawn;
        }
        else if (batch.ByAlias.TryGetValue(reportId!, out row))
        {
            // A resend of an id that was folded into an earlier report.
            if (row.ReporterUserId != batch.UserId || row.PairKey != pairKey)
            {
                return Reject(reportId, "This reportId is already used by another report.");
            }

            outcome = SongDuplicateReportOutcomes.Updated;
            reopen = row.Status == SongDuplicateReportStatus.Withdrawn;
        }
        else if (batch.ByPair.TryGetValue(pairKey, out row))
        {
            outcome = SongDuplicateReportOutcomes.Updated;
            reopen = row.Status is SongDuplicateReportStatus.Withdrawn
                or SongDuplicateReportStatus.NotDuplicate
                or SongDuplicateReportStatus.KeptBoth;
            context.SongDuplicateReportAliases.Add(new SongDuplicateReportAlias { AliasId = reportId!, ReportId = row.ReportId });
            batch.ByAlias[reportId!] = row;
        }
        else
        {
            row = new SongDuplicateReport
            {
                ReportId = reportId!,
                ReporterUserId = batch.UserId,
                PairKey = pairKey,
                Status = SongDuplicateReportStatus.Pending,
                CreatedAt = batch.Now,
            };
            context.SongDuplicateReports.Add(row);
            batch.ById[row.ReportId] = row;
            batch.ByPair[pairKey] = row;
            outcome = SongDuplicateReportOutcomes.Recorded;
            reopen = false;
        }

        if (row.Status == SongDuplicateReportStatus.Merged)
        {
            return new Applied(reportId, outcome, Row: row);
        }

        var hiddenSongId = SongDuplicateReport.HiddenSongOf(songId, otherSongId, preferredSongId);
        var changed = row.SongId != songId
            || row.OtherSongId != otherSongId
            || row.Kind != kind
            || row.PreferredSongId != preferredSongId
            || row.HiddenSongId != hiddenSongId
            || row.Note != note
            || reopen
            || row.Version == default;
        if (changed)
        {
            row.SongId = songId;
            row.OtherSongId = otherSongId;
            row.Kind = kind;
            row.PreferredSongId = preferredSongId;
            row.HiddenSongId = hiddenSongId;
            row.Note = note;
            if (reopen)
            {
                row.Status = SongDuplicateReportStatus.Pending;
                row.ResolvedAt = null;
                row.ResolvedByUserId = null;
            }
            row.Version = batch.NextVersion();
        }

        return new Applied(reportId, outcome, Row: row);
    }

    private static Applied Reject(string? reportId, string error) => new(reportId, SongDuplicateReportOutcomes.Rejected, error);

    /// <summary>Null when <paramref name="id"/> is usable as an id column value, otherwise why not.</summary>
    private static string? CheckId(string? id, string name)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return $"{name} is required.";
        }
        if (id.Length > SongDuplicateReport.MaxIdLength)
        {
            return $"{name} is longer than {SongDuplicateReport.MaxIdLength} characters.";
        }
        if (id.Contains('\0') || id.Contains('|'))
        {
            return $"{name} can't contain a NUL or '|' character.";
        }

        return null;
    }

    private static bool IsUsableId(string id) => CheckId(id, "") is null;

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
