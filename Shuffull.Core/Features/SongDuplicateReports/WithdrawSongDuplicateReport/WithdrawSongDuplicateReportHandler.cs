using MediatR;
using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Persistence;

namespace Shuffull.Core.Features.SongDuplicateReports.WithdrawSongDuplicateReport;

/// <summary>
/// Marks one of the caller's reports <see cref="SongDuplicateReportStatus.Withdrawn"/>. The row is never deleted, so
/// the undo reaches the reporter's other devices through the <c>mine</c> feed. Safe to resend: an already-withdrawn
/// report is returned as it is. So is a merged one, since a merge is final and leaves nothing to hide.
///
/// <para>One conditional UPDATE, so a curator's resolve landing at the same moment can't undo the withdraw, and a
/// withdraw can't reopen a merge. A report that doesn't exist or isn't the caller's is a 404.</para>
/// </summary>
public class WithdrawSongDuplicateReportHandler(ShuffullContext context)
    : IRequestHandler<WithdrawSongDuplicateReportCommand, Result<WithdrawSongDuplicateReportResponse>>
{
    public async Task<Result<WithdrawSongDuplicateReportResponse>> Handle(WithdrawSongDuplicateReportCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var now = DateTime.UtcNow;
            await context.SongDuplicateReports
                .Where(r => r.ReportId == request.ReportId
                    && r.ReporterUserId == request.UserId
                    && r.Status != SongDuplicateReportStatus.Withdrawn
                    && r.Status != SongDuplicateReportStatus.Merged)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(r => r.Status, SongDuplicateReportStatus.Withdrawn)
                    .SetProperty(r => r.ResolvedAt, now)
                    .SetProperty(r => r.ResolvedByUserId, request.UserId)
                    .SetProperty(r => r.Version, now), cancellationToken);

            var report = await context.SongDuplicateReports
                .AsNoTracking()
                .SingleOrDefaultAsync(r => r.ReportId == request.ReportId && r.ReporterUserId == request.UserId, cancellationToken);
            if (report is null)
            {
                return Result.Error<WithdrawSongDuplicateReportResponse>(
                    ResultErrors.NotFound($"You have no duplicate report '{request.ReportId}'."));
            }

            return Result.Ok(new WithdrawSongDuplicateReportResponse(SongDuplicateReportDto.Create(report)));
        }
        catch (Exception ex)
        {
            return Result.Error<WithdrawSongDuplicateReportResponse>(
                ResultErrors.FromException(ex, $"Could not withdraw the duplicate report. {ex.Message}"));
        }
    }
}
