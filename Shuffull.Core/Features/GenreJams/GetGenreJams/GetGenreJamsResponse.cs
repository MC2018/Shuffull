using Shuffull.Core.Models.Dtos;

namespace Shuffull.Core.Features.GenreJams.GetGenreJams;

public record GetGenreJamsResponse(IReadOnlyList<GenreJamDto> GenreJams);
