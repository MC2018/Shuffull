using FluentValidation.TestHelper;
using Shuffull.Core.Tests.Tools;
using Shuffull.Core.Features.Songs.GetSongList;

namespace Shuffull.Core.Tests.Features.Songs.GetSongList;

public class GetSongListValidatorTest
{
    private static GetSongListQuery SafeValue()
        => new(new[] { "song-123" });

    private static string[] MakeIds(int count)
        => Enumerable.Range(0, count).Select(i => $"song-{i}").ToArray();

    public static IEnumerable<object[]> CreateErrorValues() => new List<object[]>
    {
        (SafeValue() with { SongIds = null! }).ToObjectArray(),                                 // null
        (SafeValue() with { SongIds = Array.Empty<string>() }).ToObjectArray(),                 // empty
        (SafeValue() with { SongIds = MakeIds(GetSongListValidator.MaxSongIds + 1) }).ToObjectArray(), // over the cap
    }.ToArray();

    public static IEnumerable<object[]> CreateValidValues() => new List<object[]>
    {
        SafeValue().ToObjectArray(),
        (SafeValue() with { SongIds = new[] { "a", "b", "c" } }).ToObjectArray(),
        (SafeValue() with { SongIds = MakeIds(GetSongListValidator.MaxSongIds) }).ToObjectArray(), // exactly at the cap
    }.ToArray();

    [Theory]
    [MemberData(nameof(CreateErrorValues))]
    public void InvalidQuery_ProducesValidationErrors(GetSongListQuery query)
    {
        var validator = new GetSongListValidator();
        var errors = validator.TestValidate(query).Errors;
        Assert.NotEmpty(errors);
    }

    [Theory]
    [MemberData(nameof(CreateValidValues))]
    public void ValidQuery_ProducesNoErrors(GetSongListQuery query)
    {
        var validator = new GetSongListValidator();
        var errors = validator.TestValidate(query).Errors;
        Assert.Empty(errors);
    }
}
