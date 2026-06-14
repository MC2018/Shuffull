using FluentValidation.TestHelper;
using Shuffull.Core.Features.Users.GetCurrentUser;
using Shuffull.Core.Tests.Tools;

namespace Shuffull.Core.Tests.Features.Users.GetCurrentUser;

public class GetCurrentUserValidatorTest
{
    private static GetCurrentUserQuery SafeValue()
        => new("user-123");

    public static IEnumerable<object[]> CreateErrorValues() => new List<object[]>
    {
        (SafeValue() with { UserId = null! }).ToObjectArray(),
        (SafeValue() with { UserId = "" }).ToObjectArray(),
    }.ToArray();

    public static IEnumerable<object[]> CreateValidValues() => new List<object[]>
    {
        SafeValue().ToObjectArray(),
        (SafeValue() with { UserId = "another-user" }).ToObjectArray(),
    }.ToArray();

    [Theory]
    [MemberData(nameof(CreateErrorValues))]
    public void InvalidQuery_ProducesValidationErrors(GetCurrentUserQuery query)
    {
        var validator = new GetCurrentUserValidator();
        var errors = validator.TestValidate(query).Errors;
        Assert.NotEmpty(errors);
    }

    [Theory]
    [MemberData(nameof(CreateValidValues))]
    public void ValidQuery_ProducesNoErrors(GetCurrentUserQuery query)
    {
        var validator = new GetCurrentUserValidator();
        var errors = validator.TestValidate(query).Errors;
        Assert.Empty(errors);
    }
}
