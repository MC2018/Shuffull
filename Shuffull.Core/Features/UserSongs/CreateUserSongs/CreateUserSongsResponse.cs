using Shuffull.Core.Models.Dtos;

namespace Shuffull.Core.Features.UserSongs.CreateUserSongs;

/// <summary>
/// The play records that were actually created. Songs the user already tracked, or ids that match no
/// song, are skipped, so <see cref="Created"/> may be shorter than the requested id list.
/// </summary>
public record CreateUserSongsResponse(IReadOnlyList<UserSongDto> Created);
