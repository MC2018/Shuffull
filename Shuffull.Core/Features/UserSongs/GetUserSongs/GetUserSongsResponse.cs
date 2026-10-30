using Shuffull.Core.Models.Dtos;

namespace Shuffull.Core.Features.UserSongs.GetUserSongs;

/// <summary>
/// A single page of a user's play records, ordered by Version then SongId. <see cref="EndOfList"/> is false
/// when more records remain past this page, so the client can keep paging from the last record's
/// <see cref="UserSongDto.Version"/> and <see cref="UserSongDto.SongId"/>.
/// </summary>
public record GetUserSongsResponse(IReadOnlyList<UserSongDto> UserSongs, bool EndOfList);
