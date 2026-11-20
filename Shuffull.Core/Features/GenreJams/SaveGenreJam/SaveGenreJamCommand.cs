using MediatR;
using Nut.Results;
using Shuffull.Core.Models.Dtos;

namespace Shuffull.Core.Features.GenreJams.SaveGenreJam;

/// <summary>Creates the caller's jam <see cref="GenreJamId"/>, or replaces it whole if it exists.</summary>
public record SaveGenreJamCommand(
    string UserId,
    string GenreJamId,
    string Name,
    GenreJamFilters? Whitelists,
    GenreJamFilters? Blacklists,
    int? HeavinessMin,
    int? HeavinessMax) : IRequest<Result<SaveGenreJamResponse>>;
