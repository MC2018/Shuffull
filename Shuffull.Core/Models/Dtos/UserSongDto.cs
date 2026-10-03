using Nut.Results;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;

namespace Shuffull.Core.Models.Dtos;

/// <summary>
/// API-facing projection of a <see cref="UserSong"/> (the per-user play record). Built through the
/// static <see cref="Create"/> factory so mapping lives in one place and surfaces failures as a
/// <see cref="Result"/> rather than throwing.
/// </summary>
public record UserSongDto(string UserId, string SongId, DateTime LastPlayed, DateTime Version, LikeStatus LikeStatus)
{
    public static Result<UserSongDto> Create(UserSong userSong)
    {
        if (userSong is null)
        {
            return Result.Error<UserSongDto>("UserSong cannot be null.");
        }

        return Result.Ok(new UserSongDto(
            UserId: userSong.UserId,
            SongId: userSong.SongId,
            LastPlayed: userSong.LastPlayed,
            Version: userSong.Version,
            LikeStatus: userSong.LikeStatus));
    }
}
