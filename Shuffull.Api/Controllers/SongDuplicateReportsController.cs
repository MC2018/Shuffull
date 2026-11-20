using MediatR;
using Microsoft.AspNetCore.Mvc;
using Shuffull.Api.Commands.SongDuplicateReports.ReportSongDuplicates;
using Shuffull.Api.Commands.SongDuplicateReports.ResolveSongDuplicateReports;
using Shuffull.Api.Extensions;
using Shuffull.Api.Tools.Authorization;
using Shuffull.Core.Features.SongDuplicateReports.GetMySongDuplicateReports;
using Shuffull.Core.Features.SongDuplicateReports.GetSongDuplicateQueue;
using Shuffull.Core.Features.SongDuplicateReports.ReportSongDuplicates;
using Shuffull.Core.Features.SongDuplicateReports.ResolveSongDuplicateReports;
using Shuffull.Core.Features.SongDuplicateReports.WithdrawSongDuplicateReport;
using Shuffull.Core.Models.Database;

namespace Shuffull.Api.Controllers;

/// <summary>
/// Duplicate reports (shuffull-workspace#143). Any signed-in user reports a song in their library as a duplicate or an
/// unwanted version, which hides it for them alone, and can withdraw it. A curator works the queue of reported pairs.
/// The app writes through its outbox (the batch <c>POST</c> and the <c>DELETE</c> are both safe to resend) and pages
/// its own reports on <c>mine</c>. Thin controller: it resolves the authenticated user, dispatches through MediatR,
/// and maps the <see cref="Nut.Results.Result"/>; the curator gate is on the commands.
/// </summary>
[ApiController]
[Route("api/v1/song-duplicate-reports")]
public class SongDuplicateReportsController : ControllerBase
{
    private readonly IMediator _mediator;

    public SongDuplicateReportsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Any signed-in user: record reports on songs in their library. One outcome per item ("recorded", "updated" when
    /// the user already reported that pair under another id, or "rejected" with the reason).
    /// </summary>
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Report([FromBody] ReportSongDuplicatesRequest request, CancellationToken cancellationToken)
    {
        if (HttpContext.Items["User"] is not User user)
        {
            return Unauthorized();
        }

        return this.ToActionResult(await _mediator.Send(new ReportSongDuplicatesCommand(user.UserId, request.Reports ?? []), cancellationToken));
    }

    /// <summary>
    /// Any signed-in user: their reports changed after (<paramref name="afterDate"/>, <paramref name="afterId"/>),
    /// withdrawn and resolved ones included, 500 per page.
    /// </summary>
    [HttpGet("mine")]
    [Authorize]
    public async Task<IActionResult> GetMine([FromQuery] DateTime afterDate, [FromQuery] string? afterId, CancellationToken cancellationToken)
    {
        if (HttpContext.Items["User"] is not User user)
        {
            return Unauthorized();
        }

        return this.ToActionResult(await _mediator.Send(new GetMySongDuplicateReportsQuery(user.UserId, afterDate, afterId), cancellationToken));
    }

    /// <summary>
    /// Any signed-in user: withdraw their report <paramref name="reportId"/>. The row stays, marked withdrawn. 404 if
    /// they have no such report.
    /// </summary>
    [HttpDelete("{reportId}")]
    [Authorize]
    public async Task<IActionResult> Withdraw(string reportId, CancellationToken cancellationToken)
    {
        if (HttpContext.Items["User"] is not User user)
        {
            return Unauthorized();
        }

        return this.ToActionResult(await _mediator.Send(new WithdrawSongDuplicateReportCommand(user.UserId, reportId), cancellationToken));
    }

    /// <summary>Curator only: open reports grouped by pair of songs, oldest first.</summary>
    [HttpGet("queue")]
    [Authorize]
    public async Task<IActionResult> GetQueue(CancellationToken cancellationToken)
        => this.ToActionResult(await _mediator.Send(new GetSongDuplicateQueueQuery(), cancellationToken));

    /// <summary>Curator only: close every open report on a pair as "notDuplicate" or "keptBoth".</summary>
    [HttpPost("resolve")]
    [Authorize]
    public async Task<IActionResult> Resolve([FromBody] ResolveSongDuplicateReportsRequest request, CancellationToken cancellationToken)
    {
        if (HttpContext.Items["User"] is not User user)
        {
            return Unauthorized();
        }

        var command = new ResolveSongDuplicateReportsCommand(user.UserId, request.SongId, request.OtherSongId, request.Resolution);
        return this.ToActionResult(await _mediator.Send(command, cancellationToken));
    }
}
