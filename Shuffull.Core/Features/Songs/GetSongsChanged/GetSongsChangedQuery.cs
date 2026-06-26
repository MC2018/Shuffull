using MediatR;
using Nut.Results;

namespace Shuffull.Core.Features.Songs.GetSongsChanged;

public record GetSongsChangedQuery(DateTime AfterDate) : IRequest<Result<GetSongsChangedResponse>>;
