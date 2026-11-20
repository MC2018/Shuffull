using Shuffull.Core.Models.Dtos;

namespace Shuffull.Core.Features.Songs.MergeSongs;

/// <param name="Merge">The merge, as the feed sends it.</param>
/// <param name="Reports">The open reports on the pair this request closed as merged. Empty on a resend.</param>
/// <param name="AlreadyMerged">True when the loser had already been merged into this survivor, so nothing changed.</param>
public record MergeSongsResponse(SongMergeDto Merge, IReadOnlyList<SongDuplicateReportDto> Reports, bool AlreadyMerged);
