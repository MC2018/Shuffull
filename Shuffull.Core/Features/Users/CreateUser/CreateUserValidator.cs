using FluentValidation;

namespace Shuffull.Core.Features.Users.CreateUser;

public class CreateUserValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty().WithMessage("Username is required.");

        RuleFor(x => x.UserHash)
            .NotEmpty().WithMessage("Password is required.");
    }
}
