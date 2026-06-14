using MediatR;
using Nut.Results;

namespace Shuffull.Core.Features.Songs.GetSong;

public record GetSongQuery(string SongId) : IRequest<Result<GetSongResponse>>;
