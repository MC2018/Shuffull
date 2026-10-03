using FluentValidation.TestHelper;
using Shuffull.Core.Features.Songs.GetSongPage;
using Shuffull.Core.Tests.Tools;

namespace Shuffull.Core.Tests.Features.Songs.GetSongPage;

public class GetSongPageValidatorTest
{
    private static GetSongPageQuery SafeValue()
        => new(0);

    public static IEnumerable<object[]> CreateErrorValues() => new List<object[]>
    {
        (SafeValue() with { PageIndex = -1 }).ToObjectArray(),
        (SafeValue() with { PageIndex = -100 }).ToObjectArray(),
    }.ToArray();

    public static IEnumerable<object[]> CreateValidValues() => new List<object[]>
    {
        SafeValue().ToObjectArray(),
        (SafeValue() with { PageIndex = 5 }).ToObjectArray(),
    }.ToArray();

    [Theory]
    [MemberData(nameof(CreateErrorValues))]
    public void InvalidQuery_ProducesValidationErrors(GetSongPageQuery query)
    {
        var validator = new GetSongPageValidator();
        var errors = validator.TestValidate(query).Errors;
        Assert.NotEmpty(errors);
    }

    [Theory]
    [MemberData(nameof(CreateValidValues))]
    public void ValidQuery_ProducesNoErrors(GetSongPageQuery query)
    {
        var validator = new GetSongPageValidator();
        var errors = validator.TestValidate(query).Errors;
        Assert.Empty(errors);
    }
}
