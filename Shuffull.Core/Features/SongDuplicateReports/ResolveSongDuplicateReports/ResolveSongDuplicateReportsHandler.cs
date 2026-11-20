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
/// see that copy.
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
            var resolution = SongDuplicateReportWire.Parse<SongDuplicateReportStatus>(request.Resolution)!.Value;
            var pairKey = SongDuplicateReport.PairKeyOf(request.SongId!, request.OtherSongId!);

            var open = await context.SongDuplicateReports
                .Where(r => r.PairKey == pairKey && r.Status == SongDuplicateReportStatus.Pending)
                .OrderBy(r => r.CreatedAt)
                .ThenBy(r => r.ReportId)
                .ToListAsync(cancellationToken);

            var now = DateTime.UtcNow;
            for (var i = 0; i < open.Count; i++)
            {
                open[i].Status = resolution;
                open[i].ResolvedAt = now;
                open[i].ResolvedByUserId = request.UserId;
                open[i].Version = now.AddTicks(10 * i);
            }
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
