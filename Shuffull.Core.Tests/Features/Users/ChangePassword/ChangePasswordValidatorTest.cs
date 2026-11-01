using FluentValidation.TestHelper;
using Shuffull.Core.Features.Users.ChangePassword;
using Shuffull.Core.Tests.Tools;

namespace Shuffull.Core.Tests.Features.Users.ChangePassword;

public class ChangePasswordValidatorTest
{
    private static ChangePasswordCommand SafeValue()
        => new("user-1", "old-hash", "new-hash");

    public static IEnumerable<object[]> CreateErrorValues() => new List<object[]>
    {
        (SafeValue() with { UserId = null! }).ToObjectArray(),
        (SafeValue() with { UserId = "" }).ToObjectArray(),
        (SafeValue() with { CurrentUserHash = null! }).ToObjectArray(),
        (SafeValue() with { CurrentUserHash = "" }).ToObjectArray(),
        // The controller turns a missing field into "", so this is what stops an empty new hash being stored.
        (SafeValue() with { NewUserHash = null! }).ToObjectArray(),
        (SafeValue() with { NewUserHash = "" }).ToObjectArray(),
    }.ToArray();

    public static IEnumerable<object[]> CreateValidValues() => new List<object[]>
    {
        SafeValue().ToObjectArray(),
        // The same password again is allowed: it still rotates the stamp, which signs out every other device.
        (SafeValue() with { NewUserHash = "old-hash" }).ToObjectArray(),
    }.ToArray();

    [Theory]
    [MemberData(nameof(CreateErrorValues))]
    public void InvalidCommand_ProducesValidationErrors(ChangePasswordCommand command)
    {
        var validator = new ChangePasswordValidator();
        var errors = validator.TestValidate(command).Errors;
        Assert.NotEmpty(errors);
    }

    [Theory]
    [MemberData(nameof(CreateValidValues))]
    public void ValidCommand_ProducesNoErrors(ChangePasswordCommand command)
    {
        var validator = new ChangePasswordValidator();
        var errors = validator.TestValidate(command).Errors;
        Assert.Empty(errors);
    }
}
