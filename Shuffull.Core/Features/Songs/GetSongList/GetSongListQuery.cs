using MediatR;
using Nut.Results;

namespace Shuffull.Core.Features.Songs.GetSongList;

public record GetSongListQuery(IReadOnlyList<string> SongIds) : IRequest<Result<GetSongListResponse>>;
