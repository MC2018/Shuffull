using Shuffull.Core.Models.Enums;

namespace Shuffull.Core.Features.UserSongs.SetSongLikeStatus;

public record SetSongLikeStatusResponse(string SongId, LikeStatus LikeStatus);
