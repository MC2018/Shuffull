using FluentValidation;

namespace Shuffull.Core.Features.Users.AuthenticateUser;

public class AuthenticateUserValidator : AbstractValidator<AuthenticateUserCommand>
{
    public AuthenticateUserValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty().WithMessage("Username is required.");

        RuleFor(x => x.UserHash)
            .NotEmpty().WithMessage("Password is required.");
    }
}
