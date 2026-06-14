using FluentValidation.TestHelper;
using Shuffull.Core.Features.Playlists.GetPlaylists;
using Shuffull.Core.Tests.Tools;

namespace Shuffull.Core.Tests.Features.Playlists.GetPlaylists;

public class GetPlaylistsValidatorTest
{
    private static GetPlaylistsQuery SafeValue()
        => new("user-123", new[] { "playlist-123" });

    public static IEnumerable<object[]> CreateErrorValues() => new List<object[]>
    {
        (SafeValue() with { UserId = null! }).ToObjectArray(),
        (SafeValue() with { UserId = "" }).ToObjectArray(),
        (SafeValue() with { PlaylistIds = Array.Empty<string>() }).ToObjectArray(),
        (SafeValue() with { PlaylistIds = null! }).ToObjectArray(),
    }.ToArray();

    public static IEnumerable<object[]> CreateValidValues() => new List<object[]>
    {
        SafeValue().ToObjectArray(),
        (SafeValue() with { PlaylistIds = new[] { "a", "b" } }).ToObjectArray(),
    }.ToArray();

    [Theory]
    [MemberData(nameof(CreateErrorValues))]
    public void InvalidQuery_ProducesValidationErrors(GetPlaylistsQuery query)
    {
        var validator = new GetPlaylistsValidator();
        var errors = validator.TestValidate(query).Errors;
        Assert.NotEmpty(errors);
    }

    [Theory]
    [MemberData(nameof(CreateValidValues))]
    public void ValidQuery_ProducesNoErrors(GetPlaylistsQuery query)
    {
        var validator = new GetPlaylistsValidator();
        var errors = validator.TestValidate(query).Errors;
        Assert.Empty(errors);
    }
}
