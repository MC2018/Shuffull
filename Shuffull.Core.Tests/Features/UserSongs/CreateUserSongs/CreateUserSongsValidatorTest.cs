using FluentValidation.TestHelper;
using Shuffull.Core.Features.UserSongs.CreateUserSongs;
using Shuffull.Core.Tests.Tools;

namespace Shuffull.Core.Tests.Features.UserSongs.CreateUserSongs;

public class CreateUserSongsValidatorTest
{
    private static CreateUserSongsCommand SafeValue()
        => new("user-123", new[] { "song-123" });

    public static IEnumerable<object[]> CreateErrorValues() => new List<object[]>
    {
        (SafeValue() with { UserId = null! }).ToObjectArray(),
        (SafeValue() with { UserId = "" }).ToObjectArray(),
        (SafeValue() with { SongIds = Array.Empty<string>() }).ToObjectArray(),
        (SafeValue() with { SongIds = null! }).ToObjectArray(),
    }.ToArray();

    public static IEnumerable<object[]> CreateValidValues() => new List<object[]>
    {
        SafeValue().ToObjectArray(),
        (SafeValue() with { SongIds = new[] { "a", "b", "c" } }).ToObjectArray(),
    }.ToArray();

    [Theory]
    [MemberData(nameof(CreateErrorValues))]
    public void InvalidCommand_ProducesValidationErrors(CreateUserSongsCommand command)
    {
        var validator = new CreateUserSongsValidator();
        var errors = validator.TestValidate(command).Errors;
        Assert.NotEmpty(errors);
    }

    [Theory]
    [MemberData(nameof(CreateValidValues))]
    public void ValidCommand_ProducesNoErrors(CreateUserSongsCommand command)
    {
        var validator = new CreateUserSongsValidator();
        var errors = validator.TestValidate(command).Errors;
        Assert.Empty(errors);
    }
}
