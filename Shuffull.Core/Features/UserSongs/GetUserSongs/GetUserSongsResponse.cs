using Shuffull.Core.Models.Dtos;

namespace Shuffull.Core.Features.UserSongs.GetUserSongs;

/// <summary>
/// A single page of a user's play records. <see cref="EndOfList"/> is false when more records remain
/// past this page, so the client can keep paging by the last <see cref="UserSongDto.Version"/>.
/// </summary>
public record GetUserSongsResponse(IReadOnlyList<UserSongDto> UserSongs, bool EndOfList);
