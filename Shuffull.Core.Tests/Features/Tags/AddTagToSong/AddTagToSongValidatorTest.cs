using FluentValidation.TestHelper;
using Shuffull.Core.Features.Tags.AddTagToSong;
using Shuffull.Core.Tests.Tools;

namespace Shuffull.Core.Tests.Features.Tags.AddTagToSong;

public class AddTagToSongValidatorTest
{
    private static AddTagToSongCommand SafeValue()
        => new("song-123", "tag-123");

    public static IEnumerable<object[]> CreateErrorValues() => new List<object[]>
    {
        (SafeValue() with { SongId = null! }).ToObjectArray(),
        (SafeValue() with { SongId = "" }).ToObjectArray(),
        (SafeValue() with { TagId = null! }).ToObjectArray(),
        (SafeValue() with { TagId = "" }).ToObjectArray(),
    }.ToArray();

    public static IEnumerable<object[]> CreateValidValues() => new List<object[]>
    {
        SafeValue().ToObjectArray(),
        (SafeValue() with { SongId = "other-song", TagId = "other-tag" }).ToObjectArray(),
    }.ToArray();

    [Theory]
    [MemberData(nameof(CreateErrorValues))]
    public void InvalidCommand_ProducesValidationErrors(AddTagToSongCommand command)
    {
        var validator = new AddTagToSongValidator();
        var errors = validator.TestValidate(command).Errors;
        Assert.NotEmpty(errors);
    }

    [Theory]
    [MemberData(nameof(CreateValidValues))]
    public void ValidCommand_ProducesNoErrors(AddTagToSongCommand command)
    {
        var validator = new AddTagToSongValidator();
        var errors = validator.TestValidate(command).Errors;
        Assert.Empty(errors);
    }
}
