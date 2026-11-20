using Shuffull.Core.Models.Dtos;

namespace Shuffull.Api.Commands.GenreJams.SaveGenreJam;

/// <summary>Body for saving a Genre Jam: the whole jam. The id is in the route. A missing filter side reads as empty.</summary>
public record SaveGenreJamRequest(
    string? Name,
    GenreJamFilters? Whitelists,
    GenreJamFilters? Blacklists,
    int? HeavinessMin,
    int? HeavinessMax);
