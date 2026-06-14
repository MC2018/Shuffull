using FluentValidation.TestHelper;
using Shuffull.Core.Features.UserSongs.UpdateSongsLastPlayed;
using Shuffull.Core.Tests.Tools;

namespace Shuffull.Core.Tests.Features.UserSongs.UpdateSongsLastPlayed;

public class UpdateSongsLastPlayedValidatorTest
{
    private static UpdateSongsLastPlayedCommand SafeValue()
        => new("user-123", new[] { new SongLastPlayed("song-123", DateTime.UtcNow) });

    public static IEnumerable<object[]> CreateErrorValues() => new List<object[]>
    {
        (SafeValue() with { UserId = null! }).ToObjectArray(),
        (SafeValue() with { UserId = "" }).ToObjectArray(),
        (SafeValue() with { Updates = Array.Empty<SongLastPlayed>() }).ToObjectArray(),
        (SafeValue() with { Updates = null! }).ToObjectArray(),
        (SafeValue() with { Updates = new[] { new SongLastPlayed("", DateTime.UtcNow) } }).ToObjectArray(),
    }.ToArray();

    public static IEnumerable<object[]> CreateValidValues() => new List<object[]>
    {
        SafeValue().ToObjectArray(),
        (SafeValue() with { Updates = new[]
        {
            new SongLastPlayed("a", DateTime.UtcNow),
            new SongLastPlayed("b", DateTime.UtcNow),
        } }).ToObjectArray(),
    }.ToArray();

    [Theory]
    [MemberData(nameof(CreateErrorValues))]
    public void InvalidCommand_ProducesValidationErrors(UpdateSongsLastPlayedCommand command)
    {
        var validator = new UpdateSongsLastPlayedValidator();
        var errors = validator.TestValidate(command).Errors;
        Assert.NotEmpty(errors);
    }

    [Theory]
    [MemberData(nameof(CreateValidValues))]
    public void ValidCommand_ProducesNoErrors(UpdateSongsLastPlayedCommand command)
    {
        var validator = new UpdateSongsLastPlayedValidator();
        var errors = validator.TestValidate(command).Errors;
        Assert.Empty(errors);
    }
}
