using MediatR;
using Nut.Results;
using Shuffull.Core.Authentication;

namespace Shuffull.Core.Features.Songs.MergeSongs;

/// <summary>
/// A curator merges two copies of one song (Shuffull#142, epic shuffull-workspace#143): everything users decided
/// about <see cref="LoserSongId"/> moves to <see cref="SurvivorSongId"/>, and the loser is deleted. The curator picks
/// the survivor. Gated by <see cref="RequiresRoleAttribute"/>.
/// </summary>
/// <param name="UserId">The curator, recorded on the merge and the tombstone.</param>
[RequiresRole(Role.Curator)]
public record MergeSongsCommand(string UserId, string? SurvivorSongId, string? LoserSongId)
    : IRequest<Result<MergeSongsResponse>>;
