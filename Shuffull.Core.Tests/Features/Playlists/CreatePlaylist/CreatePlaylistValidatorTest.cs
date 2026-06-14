using FluentValidation.TestHelper;
using Shuffull.Core.Features.Playlists.CreatePlaylist;
using Shuffull.Core.Tests.Tools;

namespace Shuffull.Core.Tests.Features.Playlists.CreatePlaylist;

public class CreatePlaylistValidatorTest
{
    private static CreatePlaylistCommand SafeValue()
        => new("user-123", "My Playlist");

    public static IEnumerable<object[]> CreateErrorValues() => new List<object[]>
    {
        (SafeValue() with { UserId = null! }).ToObjectArray(),                                  // null user
        (SafeValue() with { UserId = "" }).ToObjectArray(),                                     // empty user
        (SafeValue() with { Name = null! }).ToObjectArray(),                                    // null name
        (SafeValue() with { Name = "" }).ToObjectArray(),                                       // empty name
        (SafeValue() with { Name = new string('x', CreatePlaylistValidator.MaxNameLength + 1) }).ToObjectArray(), // too long
    }.ToArray();

    public static IEnumerable<object[]> CreateValidValues() => new List<object[]>
    {
        SafeValue().ToObjectArray(),
        (SafeValue() with { Name = new string('x', CreatePlaylistValidator.MaxNameLength) }).ToObjectArray(), // exactly at the cap
    }.ToArray();

    [Theory]
    [MemberData(nameof(CreateErrorValues))]
    public void InvalidCommand_ProducesValidationErrors(CreatePlaylistCommand command)
    {
        var validator = new CreatePlaylistValidator();
        var errors = validator.TestValidate(command).Errors;
        Assert.NotEmpty(errors);
    }

    [Theory]
    [MemberData(nameof(CreateValidValues))]
    public void ValidCommand_ProducesNoErrors(CreatePlaylistCommand command)
    {
        var validator = new CreatePlaylistValidator();
        var errors = validator.TestValidate(command).Errors;
        Assert.Empty(errors);
    }
}
