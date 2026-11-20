using MediatR;
using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Persistence;

namespace Shuffull.Core.Features.SongDuplicateReports.ResolveSongDuplicateReports;

/// <summary>
/// Marks every pending report on one pair with the curator's verdict, in one save, each row with its own Version so
/// every reporter's devices pick the change up. Neither verdict lifts a reporter's hide: the user still chose not to
/// see that copy. The verdict is also written to the pair's <see cref="SongDuplicateVerdict"/>, in the same save, so
/// it stays on record after a reporter withdraws or reopens their report.
///
/// <para>The rows are written with their Version as the concurrency token, so a reporter withdrawing one at the same
/// moment fails the save (409) rather than being overwritten; the curator's resend then closes what is still open.
/// Resending once it is done closes nothing and succeeds.</para>
/// </summary>
public class ResolveSongDuplicateReportsHandler(ShuffullContext context)
    : IRequestHandler<ResolveSongDuplicateReportsCommand, Result<ResolveSongDuplicateReportsResponse>>
{
    public async Task<Result<ResolveSongDuplicateReportsResponse>> Handle(ResolveSongDuplicateReportsCommand request, CancellationToken cancellationToken)
    {
        try
        {
            // The validator checks all three; these guards keep the handler safe when called without it.
            if (EnumNames.Parse<SongDuplicateReportStatus>(request.Resolution) is not { } resolution
                || !SongDuplicateReportWire.Resolutions.Contains(resolution))
            {
                return Result.Error<ResolveSongDuplicateReportsResponse>(
                    new KindedError(ErrorKind.Invalid, "resolution must be \"notDuplicate\" or \"keptBoth\"."));
            }
            if (string.IsNullOrEmpty(request.SongId) || string.IsNullOrEmpty(request.OtherSongId) || request.SongId == request.OtherSongId)
            {
                return Result.Error<ResolveSongDuplicateReportsResponse>(
                    new KindedError(ErrorKind.Invalid, "songId and otherSongId must name two different songs."));
            }

            var pairKey = SongDuplicateReport.PairKeyOf(request.SongId, request.OtherSongId);

            var open = await context.SongDuplicateReports
                .Where(r => r.PairKey == pairKey && r.Status == SongDuplicateReportStatus.Pending)
                .OrderBy(r => r.CreatedAt)
                .ThenBy(r => r.ReportId)
                .ToListAsync(cancellationToken);

            if (open.Count == 0)
            {
                return Result.Ok(new ResolveSongDuplicateReportsResponse([]));
            }

            var now = SongDuplicateReport.VersionNow();
            for (var i = 0; i < open.Count; i++)
            {
                open[i].Status = resolution;
                open[i].ResolvedAt = now;
                open[i].ResolvedByUserId = request.UserId;
                open[i].Version = now.AddTicks(10 * i);
            }

            var verdict = await context.SongDuplicateVerdicts.FindAsync([pairKey], cancellationToken);
            if (verdict is null)
            {
                verdict = new SongDuplicateVerdict { PairKey = pairKey };
                context.SongDuplicateVerdicts.Add(verdict);
            }
            verdict.Verdict = resolution;
            verdict.DecidedAt = now;
            verdict.DecidedByUserId = request.UserId;

            await context.SaveChangesAsync(cancellationToken);

            return Result.Ok(new ResolveSongDuplicateReportsResponse(open.Select(SongDuplicateReportDto.Create).ToList()));
        }
        catch (Exception ex)
        {
            return Result.Error<ResolveSongDuplicateReportsResponse>(
                ResultErrors.FromException(ex, $"Could not resolve the duplicate reports; none were changed. {ex.Message}"));
        }
    }
}
