using MediatR;
using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Persistence;

namespace Shuffull.Core.Features.SongDuplicateReports.GetMySongDuplicateReports;

/// <summary>
/// The caller's duplicate reports changed after the device's cursor, so a hide made on one device holds on all of
/// them. Pages on the keyset (Version, ReportId), which steps through a tie of any size.
/// </summary>
public class GetMySongDuplicateReportsHandler(ShuffullContext context)
    : IRequestHandler<GetMySongDuplicateReportsQuery, Result<GetMySongDuplicateReportsResponse>>
{
    public const int PageSize = 500;

    public async Task<Result<GetMySongDuplicateReportsResponse>> Handle(GetMySongDuplicateReportsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var afterDate = request.AfterDate.Kind switch
            {
                DateTimeKind.Utc => request.AfterDate,
                DateTimeKind.Unspecified => DateTime.SpecifyKind(request.AfterDate, DateTimeKind.Utc),
                _ => request.AfterDate.ToUniversalTime(),
            };
            var afterId = string.IsNullOrEmpty(request.AfterId) ? null : request.AfterId;

            var mine = context.SongDuplicateReports.AsNoTracking().Where(r => r.ReporterUserId == request.UserId);
            var rows = await (afterId is null
                    ? mine.Where(r => r.Version > afterDate)
                    : mine.Where(r => r.Version > afterDate || (r.Version == afterDate && string.Compare(r.ReportId, afterId) > 0)))
                .OrderBy(r => r.Version)
                .ThenBy(r => r.ReportId)
                .Take(PageSize + 1)
                .ToListAsync(cancellationToken);

            // One row past the page means another page remains.
            var endOfList = rows.Count <= PageSize;
            return Result.Ok(new GetMySongDuplicateReportsResponse(
                rows.Take(PageSize).Select(SongDuplicateReportDto.Create).ToList(), endOfList));
        }
        catch (Exception ex)
        {
            return Result.Error<GetMySongDuplicateReportsResponse>(
                ResultErrors.FromException(ex, $"Could not load your duplicate reports. {ex.Message}"));
        }
    }
}
