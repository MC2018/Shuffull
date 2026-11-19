using Shuffull.Core.Features.GenreJams.SaveGenreJam;
using Shuffull.Core.Models.Dtos;

namespace Shuffull.Core.Tests.Features.GenreJams.SaveGenreJam;

public class SaveGenreJamValidatorTest
{
    private static readonly SaveGenreJamCommand Valid =
        new("user", "01jbz9x7k3m4n5p6q7r8s9t0v1", "Loud", new GenreJamFilters(GenreIds: ["rock"]), null, 6, 10);

    private readonly SaveGenreJamValidator _validator = new();

    [Fact]
    public void Valid_Passes() => Assert.True(_validator.Validate(Valid).IsValid);

    [Fact]
    public void NoFiltersOrHeaviness_Passes()
        => Assert.True(_validator.Validate(Valid with { Whitelists = null, Blacklists = null, HeavinessMin = null, HeavinessMax = null }).IsValid);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a\0b")]
    public void BadName_Fails(string name) => Assert.False(_validator.Validate(Valid with { Name = name }).IsValid);

    [Fact]
    public void LongName_Fails() => Assert.False(_validator.Validate(Valid with { Name = new string('a', 101) }).IsValid);

    [Theory]
    [InlineData("")]
    [InlineData("a\0b")]
    public void BadId_Fails(string id) => Assert.False(_validator.Validate(Valid with { GenreJamId = id }).IsValid);

    [Fact]
    public void LongId_Fails() => Assert.False(_validator.Validate(Valid with { GenreJamId = new string('a', 65) }).IsValid);

    [Theory]
    [InlineData(0, null)]
    [InlineData(null, 11)]
    [InlineData(8, 3)]
    public void BadHeaviness_Fails(int? min, int? max)
        => Assert.False(_validator.Validate(Valid with { HeavinessMin = min, HeavinessMax = max }).IsValid);

    [Fact]
    public void TooManyIds_Fails()
        => Assert.False(_validator.Validate(Valid with
        {
            Blacklists = new GenreJamFilters(ArtistIds: Enumerable.Range(0, SaveGenreJamValidator.MaxIdsPerList + 1).Select(i => $"a{i}").ToList()),
        }).IsValid);

    [Theory]
    [InlineData("")]
    [InlineData("a\0b")]
    public void BadFilterId_Fails(string id)
        => Assert.False(_validator.Validate(Valid with { Whitelists = new GenreJamFilters(MoodIds: [id]) }).IsValid);
}
