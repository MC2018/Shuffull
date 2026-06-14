using FluentValidation.TestHelper;
using Shuffull.Core.Features.Playlists.GetPlaylistDetails;
using Shuffull.Core.Tests.Tools;

namespace Shuffull.Core.Tests.Features.Playlists.GetPlaylistDetails;

public class GetPlaylistDetailsValidatorTest
{
    private static GetPlaylistDetailsQuery SafeValue()
        => new("user-123", "playlist-123");

    public static IEnumerable<object[]> CreateErrorValues() => new List<object[]>
    {
        (SafeValue() with { UserId = null! }).ToObjectArray(),
        (SafeValue() with { UserId = "" }).ToObjectArray(),
        (SafeValue() with { PlaylistId = null! }).ToObjectArray(),
        (SafeValue() with { PlaylistId = "" }).ToObjectArray(),
    }.ToArray();

    public static IEnumerable<object[]> CreateValidValues() => new List<object[]>
    {
        SafeValue().ToObjectArray(),
        (SafeValue() with { UserId = "other-user", PlaylistId = "other-playlist" }).ToObjectArray(),
    }.ToArray();

    [Theory]
    [MemberData(nameof(CreateErrorValues))]
    public void InvalidQuery_ProducesValidationErrors(GetPlaylistDetailsQuery query)
    {
        var validator = new GetPlaylistDetailsValidator();
        var errors = validator.TestValidate(query).Errors;
        Assert.NotEmpty(errors);
    }

    [Theory]
    [MemberData(nameof(CreateValidValues))]
    public void ValidQuery_ProducesNoErrors(GetPlaylistDetailsQuery query)
    {
        var validator = new GetPlaylistDetailsValidator();
        var errors = validator.TestValidate(query).Errors;
        Assert.Empty(errors);
    }
}
