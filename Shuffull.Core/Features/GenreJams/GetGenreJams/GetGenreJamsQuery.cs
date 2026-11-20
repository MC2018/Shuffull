using MediatR;
using Nut.Results;

namespace Shuffull.Core.Features.GenreJams.GetGenreJams;

public record GetGenreJamsQuery(string UserId) : IRequest<Result<GetGenreJamsResponse>>;
