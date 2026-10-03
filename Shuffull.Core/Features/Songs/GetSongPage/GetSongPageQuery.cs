using MediatR;
using Nut.Results;

namespace Shuffull.Core.Features.Songs.GetSongPage;

public record GetSongPageQuery(int PageIndex) : IRequest<Result<GetSongPageResponse>>;
