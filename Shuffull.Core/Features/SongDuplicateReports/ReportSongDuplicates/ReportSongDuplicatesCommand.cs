using MediatR;
using Nut.Results;

namespace Shuffull.Core.Features.SongDuplicateReports.ReportSongDuplicates;

/// <summary>
/// One report as the app's outbox sends it. <see cref="ReportId"/> is the app's id for it. <see cref="Kind"/> is
/// "duplicate" (needs <see cref="OtherSongId"/>) or "differentVersion" (with or without one).
/// <see cref="PreferredSongId"/> is the copy to keep, one of the two songs, or null to let the curator decide.
/// </summary>
public record SongDuplicateReportItem(
    string? ReportId,
    string? SongId,
    string? OtherSongId,
    string? Kind,
    string? PreferredSongId,
    string? Note);

/// <summary>
/// Records the caller's duplicate reports, each with its own outcome. Any signed-in user may report songs in their own
/// library; deliberately not <c>[RequiresRole]</c>, since a report and its hide are the user's own decision.
/// </summary>
public record ReportSongDuplicatesCommand(string UserId, IReadOnlyList<SongDuplicateReportItem> Reports)
    : IRequest<Result<ReportSongDuplicatesResponse>>;
