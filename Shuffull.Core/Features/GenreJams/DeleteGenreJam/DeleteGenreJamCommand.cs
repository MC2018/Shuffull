using MediatR;
using Nut.Results;

namespace Shuffull.Core.Features.GenreJams.DeleteGenreJam;

public record DeleteGenreJamCommand(string UserId, string GenreJamId) : IRequest<Result<DeleteGenreJamResponse>>;
