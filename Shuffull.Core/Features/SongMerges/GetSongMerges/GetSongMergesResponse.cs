using Shuffull.Core.Models.Dtos;

namespace Shuffull.Core.Features.SongMerges.GetSongMerges;

/// <summary>
/// One page of merges, ordered by Version then LoserSongId. <see cref="EndOfList"/> is false when more remain: ask
/// again with the last merge's exact <c>version</c> and <c>loserSongId</c>.
/// </summary>
public record GetSongMergesResponse(IReadOnlyList<SongMergeDto> Merges, bool EndOfList);
