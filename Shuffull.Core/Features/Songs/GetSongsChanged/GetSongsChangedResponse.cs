using Shuffull.Core.Models.Dtos;

namespace Shuffull.Core.Features.Songs.GetSongsChanged;

/// <summary>
/// A single page of songs changed since the client's cursor, ordered by <see cref="SongDto.Version"/> then
/// <see cref="SongDto.SongId"/>. <see cref="EndOfList"/> is false when more changed songs remain past this page,
/// so the client can keep paging from the last song's version and id.
/// </summary>
public record GetSongsChangedResponse(IReadOnlyList<SongDto> Songs, bool EndOfList);
