using Shuffull.Core.Models.Dtos;

namespace Shuffull.Core.Features.Songs.GetSongPage;

public record GetSongPageResponse(int PageIndex, IReadOnlyList<SongDto> Songs);
