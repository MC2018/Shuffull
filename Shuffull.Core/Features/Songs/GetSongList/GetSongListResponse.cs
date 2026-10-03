using Shuffull.Core.Models.Dtos;

namespace Shuffull.Core.Features.Songs.GetSongList;

public record GetSongListResponse(IReadOnlyList<SongDto> Songs);
