using FluentValidation.TestHelper;
using Shuffull.Core.Features.Users.AuthenticateUser;
using Shuffull.Core.Tests.Tools;

namespace Shuffull.Core.Tests.Features.Users.AuthenticateUser;

public class AuthenticateUserValidatorTest
{
    private static AuthenticateUserCommand SafeValue()
        => new("alice", "client-hash");

    public static IEnumerable<object[]> CreateErrorValues() => new List<object[]>
    {
        (SafeValue() with { Username = null! }).ToObjectArray(),
        (SafeValue() with { Username = "" }).ToObjectArray(),
        (SafeValue() with { UserHash = null! }).ToObjectArray(),
        (SafeValue() with { UserHash = "" }).ToObjectArray(),
    }.ToArray();

    public static IEnumerable<object[]> CreateValidValues() => new List<object[]>
    {
        SafeValue().ToObjectArray(),
        (SafeValue() with { Username = "bob", UserHash = "another-hash" }).ToObjectArray(),
    }.ToArray();

    [Theory]
    [MemberData(nameof(CreateErrorValues))]
    public void InvalidCommand_ProducesValidationErrors(AuthenticateUserCommand command)
    {
        var validator = new AuthenticateUserValidator();
        var errors = validator.TestValidate(command).Errors;
        Assert.NotEmpty(errors);
    }

    [Theory]
    [MemberData(nameof(CreateValidValues))]
    public void ValidCommand_ProducesNoErrors(AuthenticateUserCommand command)
    {
        var validator = new AuthenticateUserValidator();
        var errors = validator.TestValidate(command).Errors;
        Assert.Empty(errors);
    }
}
