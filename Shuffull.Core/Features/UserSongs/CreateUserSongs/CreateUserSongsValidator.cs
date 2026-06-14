using FluentValidation;

namespace Shuffull.Core.Features.UserSongs.CreateUserSongs;

public class CreateUserSongsValidator : AbstractValidator<CreateUserSongsCommand>
{
    public CreateUserSongsValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User is required.");

        RuleFor(x => x.SongIds)
            .NotEmpty().WithMessage("Song IDs were not provided.");
    }
}
