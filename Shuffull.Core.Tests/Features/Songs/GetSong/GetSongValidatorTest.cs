using FluentValidation.TestHelper;
using Shuffull.Core.Tests.Tools;
using Shuffull.Core.Features.Songs.GetSong;

namespace Shuffull.Core.Tests.Features.Songs.GetSong;

public class GetSongValidatorTest
{
    private static GetSongQuery SafeValue()
        => new("song-123");

    public static IEnumerable<object[]> CreateErrorValues() => new List<object[]>
    {
        (SafeValue() with { SongId = null! }).ToObjectArray(),  // null
        (SafeValue() with { SongId = "" }).ToObjectArray(),     // empty
    }.ToArray();

    public static IEnumerable<object[]> CreateValidValues() => new List<object[]>
    {
        SafeValue().ToObjectArray(),
        (SafeValue() with { SongId = "other-song-id" }).ToObjectArray(),
    }.ToArray();

    [Theory]
    [MemberData(nameof(CreateErrorValues))]
    public void InvalidQuery_ProducesValidationErrors(GetSongQuery query)
    {
        var validator = new GetSongValidator();
        var errors = validator.TestValidate(query).Errors;
        Assert.NotEmpty(errors);
    }

    [Theory]
    [MemberData(nameof(CreateValidValues))]
    public void ValidQuery_ProducesNoErrors(GetSongQuery query)
    {
        var validator = new GetSongValidator();
        var errors = validator.TestValidate(query).Errors;
        Assert.Empty(errors);
    }
}
