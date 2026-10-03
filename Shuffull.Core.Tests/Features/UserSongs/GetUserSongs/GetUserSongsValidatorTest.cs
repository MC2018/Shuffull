using FluentValidation.TestHelper;
using Shuffull.Core.Features.UserSongs.GetUserSongs;
using Shuffull.Core.Tests.Tools;

namespace Shuffull.Core.Tests.Features.UserSongs.GetUserSongs;

public class GetUserSongsValidatorTest
{
    private static GetUserSongsQuery SafeValue()
        => new("user-123", DateTime.UtcNow);

    public static IEnumerable<object[]> CreateErrorValues() => new List<object[]>
    {
        (SafeValue() with { UserId = null! }).ToObjectArray(),
        (SafeValue() with { UserId = "" }).ToObjectArray(),
    }.ToArray();

    public static IEnumerable<object[]> CreateValidValues() => new List<object[]>
    {
        SafeValue().ToObjectArray(),
        (SafeValue() with { AfterDate = DateTime.MinValue }).ToObjectArray(),
    }.ToArray();

    [Theory]
    [MemberData(nameof(CreateErrorValues))]
    public void InvalidQuery_ProducesValidationErrors(GetUserSongsQuery query)
    {
        var validator = new GetUserSongsValidator();
        var errors = validator.TestValidate(query).Errors;
        Assert.NotEmpty(errors);
    }

    [Theory]
    [MemberData(nameof(CreateValidValues))]
    public void ValidQuery_ProducesNoErrors(GetUserSongsQuery query)
    {
        var validator = new GetUserSongsValidator();
        var errors = validator.TestValidate(query).Errors;
        Assert.Empty(errors);
    }
}
