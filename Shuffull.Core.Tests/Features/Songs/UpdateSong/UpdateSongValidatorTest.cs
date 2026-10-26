using FluentValidation.TestHelper;
using Shuffull.Core.Features.Songs.UpdateSong;
using Shuffull.Shared.Enums;

namespace Shuffull.Core.Tests.Features.Songs.UpdateSong;

public class UpdateSongValidatorTest
{
    private readonly UpdateSongValidator _validator = new();

    private static UpdateSongCommand Valid() =>
        new("song-1", "A Name", Bpm: 128, Heaviness: 6,
            Artists: new[] { "Artist" },
            Tags: new[] { new SongTagEdit("House", TagType.Genre) });

    [Fact]
    public void Valid_PassesWithNoErrors()
    {
        Assert.Empty(_validator.TestValidate(Valid()).Errors);
    }

    [Fact]
    public void Valid_AllowsNullOptionalsAndEmptyCollections()
    {
        var cmd = Valid() with { Bpm = null, Heaviness = null, Artists = Array.Empty<string>(), Tags = Array.Empty<SongTagEdit>() };
        Assert.Empty(_validator.TestValidate(cmd).Errors);
    }

    [Fact]
    public void MissingSongId_Fails()
    {
        _validator.TestValidate(Valid() with { SongId = "" }).ShouldHaveValidationErrorFor(x => x.SongId);
    }

    [Fact]
    public void MissingName_Fails()
    {
        _validator.TestValidate(Valid() with { Name = "" }).ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(5000)]
    public void OutOfRangeBpm_Fails(int bpm)
    {
        _validator.TestValidate(Valid() with { Bpm = bpm }).ShouldHaveValidationErrorFor(x => x.Bpm);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(-1)]
    public void OutOfRangeHeaviness_Fails(int heaviness)
    {
        _validator.TestValidate(Valid() with { Heaviness = heaviness }).ShouldHaveValidationErrorFor(x => x.Heaviness);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public void HeavinessAtBounds_Passes(int heaviness)
    {
        Assert.Empty(_validator.TestValidate(Valid() with { Heaviness = heaviness }).Errors);
    }

    [Fact]
    public void BlankArtistEntry_Fails()
    {
        _validator.TestValidate(Valid() with { Artists = new[] { "" } }).ShouldHaveValidationErrorFor("Artists[0]");
    }

    [Fact]
    public void BlankTagName_Fails()
    {
        var cmd = Valid() with { Tags = new[] { new SongTagEdit("", TagType.Genre) } };
        Assert.NotEmpty(_validator.TestValidate(cmd).Errors);
    }

    [Fact]
    public void InvalidTagType_Fails()
    {
        var cmd = Valid() with { Tags = new[] { new SongTagEdit("X", (TagType)999) } };
        Assert.NotEmpty(_validator.TestValidate(cmd).Errors);
    }
}
