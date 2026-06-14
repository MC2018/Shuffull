using FluentValidation.TestHelper;
using Shuffull.Core.Features.Playlists.GetUserPlaylists;
using Shuffull.Core.Tests.Tools;

namespace Shuffull.Core.Tests.Features.Playlists.GetUserPlaylists;

public class GetUserPlaylistsValidatorTest
{
    private static GetUserPlaylistsQuery SafeValue()
        => new("user-123");

    public static IEnumerable<object[]> CreateErrorValues() => new List<object[]>
    {
        (SafeValue() with { UserId = null! }).ToObjectArray(),
        (SafeValue() with { UserId = "" }).ToObjectArray(),
    }.ToArray();

    public static IEnumerable<object[]> CreateValidValues() => new List<object[]>
    {
        SafeValue().ToObjectArray(),
        (SafeValue() with { UserId = "other-user" }).ToObjectArray(),
    }.ToArray();

    [Theory]
    [MemberData(nameof(CreateErrorValues))]
    public void InvalidQuery_ProducesValidationErrors(GetUserPlaylistsQuery query)
    {
        var validator = new GetUserPlaylistsValidator();
        var errors = validator.TestValidate(query).Errors;
        Assert.NotEmpty(errors);
    }

    [Theory]
    [MemberData(nameof(CreateValidValues))]
    public void ValidQuery_ProducesNoErrors(GetUserPlaylistsQuery query)
    {
        var validator = new GetUserPlaylistsValidator();
        var errors = validator.TestValidate(query).Errors;
        Assert.Empty(errors);
    }
}
