using FluentValidation;

namespace Shuffull.Core.Features.Users.ChangePassword;

public class ChangePasswordValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User is required.");

        RuleFor(x => x.CurrentUserHash)
            .NotEmpty().WithMessage("Current password is required.");

        RuleFor(x => x.NewUserHash)
            .NotEmpty().WithMessage("New password is required.");
    }
}
