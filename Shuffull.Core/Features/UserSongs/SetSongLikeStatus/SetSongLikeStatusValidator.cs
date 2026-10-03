using FluentValidation;

namespace Shuffull.Core.Features.UserSongs.SetSongLikeStatus;

public class SetSongLikeStatusValidator : AbstractValidator<SetSongLikeStatusCommand>
{
    public SetSongLikeStatusValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User is required.");

        RuleFor(x => x.SongId)
            .NotEmpty().WithMessage("Song ID is required.");

        RuleFor(x => x.LikeStatus)
            .IsInEnum().WithMessage("Invalid like status.");
    }
}
