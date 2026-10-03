using FluentValidation;

namespace Shuffull.Core.Features.UserSongs.GetUserSongs;

public class GetUserSongsValidator : AbstractValidator<GetUserSongsQuery>
{
    public GetUserSongsValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User is required.");
    }
}
