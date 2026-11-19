using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Models.Dtos;

/// <summary>A saved Genre Jam as the API sends it. <see cref="Version"/> is when it was last saved.</summary>
public record GenreJamDto(
    string GenreJamId,
    string Name,
    GenreJamFilters Whitelists,
    GenreJamFilters Blacklists,
    int? HeavinessMin,
    int? HeavinessMax,
    DateTime Version)
{
    public static GenreJamDto Create(GenreJam jam) => new(
        jam.GenreJamId,
        jam.Name,
        jam.Whitelists.Normalized(),
        jam.Blacklists.Normalized(),
        jam.HeavinessMin,
        jam.HeavinessMax,
        jam.Version);
}
